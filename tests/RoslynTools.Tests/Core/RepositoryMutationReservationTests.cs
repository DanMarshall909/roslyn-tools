using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class RepositoryMutationReservationTests
{
    [Fact]
    public async Task an_exclusive_repository_reservation_survives_separate_provider_instances()
    {
        using var fixture = new ReservationFixture();
        var firstProvider = fixture.CreateProvider();
        var secondProvider = fixture.CreateProvider();
        var repository = fixture.CreateRepository("repository-001");

        using var first = await firstProvider.AcquireAsync(repository);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await secondProvider.AcquireAsync(repository, cancellation.Token));

        first.Dispose();
        using var second = await secondProvider.AcquireAsync(repository);
        second.ShouldNotBeNull();
        second.FenceEpoch.ShouldBeGreaterThan(first.FenceEpoch);
    }

    [Fact]
    public async Task another_repository_can_be_reserved_while_the_first_is_held()
    {
        using var fixture = new ReservationFixture();
        using var first = await fixture.CreateProvider().AcquireAsync(
            fixture.CreateRepository("repository-001"));

        using var second = await fixture.CreateProvider().AcquireAsync(
            fixture.CreateRepository("repository-002"));

        second.ShouldNotBeNull();
    }

    [Fact]
    public async Task absent_protection_proof_cannot_create_a_reservation()
    {
        using var fixture = new ReservationFixture();
        var authority = new TestProtectionAuthority(allow: false, provideLease: true);
        var provider = new RepositoryMutationReservationProvider(
            fixture.ControlRoot,
            authority);

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await provider.AcquireAsync(fixture.CreateRepository("repository-001")));

        Directory.Exists(fixture.ControlRoot).ShouldBeFalse();
        authority.LastLease!.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task a_protection_claim_without_a_lease_fails_closed()
    {
        using var fixture = new ReservationFixture();
        var provider = new RepositoryMutationReservationProvider(
            fixture.ControlRoot,
            new TestProtectionAuthority(allow: true, provideLease: false));

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await provider.AcquireAsync(fixture.CreateRepository("repository-001")));

        Directory.Exists(fixture.ControlRoot).ShouldBeFalse();
    }

    [Fact]
    public async Task local_host_reservation_uses_its_private_control_root()
    {
        using var fixture = new ReservationFixture();
        var authority = new LocalDurableOperationAuthority(fixture.ControlRoot);
        var provider = new RepositoryMutationReservationProvider(
            authority.MutationReservationRoot,
            authority);

        using var reservation = await provider.AcquireAsync(
            fixture.CreateRepository("repository-001"));

        reservation.AssertHeld("repository-001");
        Directory.Exists(authority.MutationReservationRoot).ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => reservation.AssertHeld("repository-002"));
        reservation.Dispose();
        Should.Throw<ObjectDisposedException>(() => reservation.AssertHeld("repository-001"));
    }

    [Fact]
    public async Task local_host_rejects_an_overlapping_repository_root()
    {
        using var fixture = new ReservationFixture();
        var authority = new LocalDurableOperationAuthority(fixture.ControlRoot);
        var provider = new RepositoryMutationReservationProvider(
            authority.MutationReservationRoot,
            authority);
        var repository = new ChangePlanRepository(
            "repository-001",
            Path.Combine(fixture.ControlRoot, "repository"),
            Path.Combine(fixture.ControlRoot, "repository"),
            "test-root-identity",
            "test-revision",
            new string('a', 64));

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await provider.AcquireAsync(repository));

        Directory.Exists(authority.MutationReservationRoot).ShouldBeFalse();
    }

    [Fact]
    public async Task an_external_process_lock_blocks_reservation_until_owner_exit_on_linux()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/flock"))
            return;

        using var fixture = new ReservationFixture();
        var repository = fixture.CreateRepository("repository-001");
        var provider = fixture.CreateProvider();
        using (await provider.AcquireAsync(repository))
        {
        }
        var digest = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(repository.RepositoryId))).ToLowerInvariant();
        var lockPath = Path.Combine(fixture.ControlRoot, digest + ".reservation");
        var start = new ProcessStartInfo("/usr/bin/flock")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("-x");
        start.ArgumentList.Add(lockPath);
        start.ArgumentList.Add("sh");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("printf 'locked\\n'; sleep 10");
        using var holder = Process.Start(start)!;
        try
        {
            (await holder.StandardOutput.ReadLineAsync()).ShouldBe("locked");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Should.ThrowAsync<OperationCanceledException>(async () =>
                await provider.AcquireAsync(repository, cancellation.Token));
        }
        finally
        {
            holder.Kill(entireProcessTree: true);
            await holder.WaitForExitAsync();
        }

        using var next = await provider.AcquireAsync(repository);
        next.AssertHeld(repository.RepositoryId);
    }

    [Fact]
    public async Task a_corrupt_durable_epoch_blocks_a_new_owner()
    {
        using var fixture = new ReservationFixture();
        var repository = fixture.CreateRepository("repository-001");
        using (await fixture.CreateProvider().AcquireAsync(repository))
        {
        }
        File.WriteAllText(fixture.EpochPath(repository.RepositoryId), "invalid");

        await Should.ThrowAsync<InvalidDataException>(async () =>
            await fixture.CreateProvider().AcquireAsync(repository));
    }

    [Fact]
    public async Task an_uncertain_epoch_flush_cannot_reuse_its_issued_value()
    {
        using var fixture = new ReservationFixture();
        var repository = fixture.CreateRepository("repository-001");
        var failing = new RepositoryMutationReservationProvider(
            fixture.ControlRoot,
            new TestProtectionAuthority(allow: true),
            new FlushFailingDurability());

        await Should.ThrowAsync<IOException>(async () =>
            await failing.AcquireAsync(repository));
        using var restarted = await fixture.CreateProvider().AcquireAsync(repository);

        restarted.FenceEpoch.ShouldBe(2);
    }

    private sealed class ReservationFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "rt-reservation-test-" + Guid.NewGuid().ToString("N"));

        internal string ControlRoot => Path.Combine(_root, "control");

        internal RepositoryMutationReservationProvider CreateProvider() => new(
            ControlRoot,
            new TestProtectionAuthority(allow: true));

        internal ChangePlanRepository CreateRepository(string repositoryId) => new(
            repositoryId,
            Path.Combine(_root, "repository"),
            Path.Combine(_root, "repository"),
            "test-root-identity",
            "test-revision",
            new string('a', 64));

        internal string EpochPath(string repositoryId)
        {
            var digest = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(repositoryId))).ToLowerInvariant();
            return Path.Combine(ControlRoot, digest + ".epoch");
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestProtectionAuthority(bool allow, bool provideLease = true)
        : IRepositoryMutationReservationProtectionAuthority
    {
        internal TestProtectionLease? LastLease { get; private set; }

        public bool TryAcquire(
            string controlRoot,
            ChangePlanRepository repository,
            out IDisposable? protectionLease)
        {
            LastLease = provideLease ? new TestProtectionLease() : null;
            protectionLease = LastLease;
            if (!allow || !provideLease)
            {
                return allow;
            }

            Directory.CreateDirectory(controlRoot);
            return true;
        }
    }

    private sealed class TestProtectionLease : IDisposable
    {
        internal bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class FlushFailingDurability : IOperationLedgerDurability
    {
        public void FlushFileMetadata(string path)
        {
        }

        public void ReplaceFile(string temporaryPath, string recordPath) =>
            File.Move(temporaryPath, recordPath, overwrite: true);

        public void FlushDirectoryMetadata(string directory) =>
            throw new IOException("Injected epoch directory-flush failure.");
    }
}
