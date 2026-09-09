using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class RepositoryTrustStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"rt-trust-store-{Guid.NewGuid():N}");

    public RepositoryTrustStoreTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void absent_registry_is_empty_without_creating_configuration()
    {
        var store = CreateStore();
        store.Read().ShouldBeEmpty();
        Directory.Exists(Path.GetDirectoryName(store.FilePath)).ShouldBeFalse();
    }

    [Fact]
    public void multiple_roots_are_deduplicated_canonicalized_and_persisted()
    {
        var store = CreateStore();
        var first = CreateRoot("first");
        var second = CreateRoot("second");
        store.Add(first).ShouldBeTrue();
        store.Add(Path.Combine(first, ".")).ShouldBeFalse();
        store.Add(second).ShouldBeTrue();
        new RepositoryTrustStore(store.FilePath).Read().ShouldBe([first, second]);
        if (OperatingSystem.IsWindows())
            store.Add(first.ToUpperInvariant()).ShouldBeFalse();
    }

    [Fact]
    public void returned_roots_cannot_mutate_store_or_previous_snapshots()
    {
        var store = CreateStore();
        var first = CreateRoot("first");
        store.Add(first);
        var original = store.Read();
        var list = original.ShouldBeAssignableTo<IList<string>>();
        Should.Throw<NotSupportedException>(() => list[0] = "changed");
        store.Add(CreateRoot("second"));
        original.ShouldBe([first]);
    }

    [Fact]
    public void removal_is_exact_and_can_revoke_a_deleted_checkout()
    {
        var store = CreateStore();
        var parent = CreateRoot("parent");
        var child = CreateRoot(Path.Combine("parent", "child"));
        store.Add(parent);
        store.Add(child);
        Directory.Delete(child);
        store.Remove(Path.Combine(child, ".")).ShouldBeTrue();
        store.Read().ShouldBe([parent]);
        store.Remove(child).ShouldBeFalse();
        Directory.Exists(parent).ShouldBeTrue();
    }

    [Fact]
    public void add_rejects_relative_missing_and_file_roots_without_creating_registry()
    {
        var store = CreateStore();
        Should.Throw<ArgumentException>(() => store.Add("relative"));
        Should.Throw<ArgumentException>(() => store.Add(Path.Combine(_directory, "missing")));
        var file = Path.Combine(_directory, "file.cs");
        File.WriteAllText(file, "class Example {}");
        Should.Throw<ArgumentException>(() => store.Add(file));
        File.Exists(store.FilePath).ShouldBeFalse();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"version\":2,\"roots\":[]}")]
    [InlineData("{\"version\":1,\"roots\":null}")]
    [InlineData("{\"version\":1,\"roots\":[null]}")]
    [InlineData("{\"version\":1,\"roots\":[\"relative\"]}")]
    [InlineData("{\"version\":1,\"roots\":[],\"trustAll\":true}")]
    public void malformed_registry_fails_closed_and_is_never_overwritten(string contents)
    {
        var store = CreateStore();
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, contents);
        Should.Throw<InvalidDataException>(() => store.Read());
        Should.Throw<InvalidDataException>(() => store.Add(CreateRoot("root")));
        File.ReadAllText(store.FilePath).ShouldBe(contents);
        var error = new RepositoryTrustService(store).List().Match<RtError?>(_ => null, failure => failure);
        error.ShouldNotBeNull().Kind.ShouldBe(RtErrorKind.TrustRequired);
    }

    [Fact]
    public async Task independent_store_instances_do_not_lose_concurrent_updates()
    {
        var store = CreateStore();
        var roots = Enumerable.Range(0, 12).Select(index => CreateRoot($"repo-{index:D2}")).ToArray();
        await Task.WhenAll(roots.Select(root => Task.Run(() => new RepositoryTrustStore(store.FilePath).Add(root))));
        store.Read().ShouldBe(roots);
        await Task.WhenAll(roots.Where((_, index) => index % 2 == 0)
            .Select(root => Task.Run(() => new RepositoryTrustStore(store.FilePath).Remove(root))));
        store.Read().ShouldBe(roots.Where((_, index) => index % 2 != 0));
        Directory.GetFiles(Path.GetDirectoryName(store.FilePath)!, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public async Task waiting_update_can_be_cancelled_without_changing_approvals()
    {
        var store = CreateStore();
        var existing = CreateRoot("existing");
        store.Add(existing);
        using var heldLock = new FileStream(store.FilePath + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
        await Should.ThrowAsync<OperationCanceledException>(() => Task.Run(() => store.Add(CreateRoot("new"), cancellation.Token)));
        store.Read().ShouldBe([existing]);
    }

    [Fact]
    public void persisted_roots_can_be_listed_after_checkout_is_deleted()
    {
        var store = CreateStore();
        var root = CreateRoot("gone");
        store.Add(root);
        Directory.Delete(root);
        store.Read().ShouldBe([root]);
    }

    [SkippableFact]
    public void linked_roots_and_registry_locations_are_rejected()
    {
        var actual = CreateRoot("actual");
        var link = Path.Combine(_directory, "linked");
        try
        {
            Directory.CreateSymbolicLink(link, actual);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Skip.If(true, $"Creating a symlink is unavailable: {exception.Message}");
        }
        var store = CreateStore();
        Should.Throw<IOException>(() => store.Add(link));
        var linkedStore = new RepositoryTrustStore(Path.Combine(link, "registry.json"));
        Should.Throw<IOException>(() => linkedStore.Read());
        Should.Throw<IOException>(() => linkedStore.Add(actual));
        File.Exists(Path.Combine(actual, "registry.json")).ShouldBeFalse();
    }

    [Fact]
    public void unix_registry_permissions_are_private()
    {
        if (OperatingSystem.IsWindows())
            return;
        var store = CreateStore();
        store.Add(CreateRoot("root"));
        File.GetUnixFileMode(store.FilePath).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var document = JsonDocument.Parse(File.ReadAllText(store.FilePath));
        document.RootElement.GetProperty("version").GetInt32().ShouldBe(1);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private RepositoryTrustStore CreateStore() => new(Path.Combine(_directory, "config", "trusted-roots.json"));

    private string CreateRoot(string name) => Directory.CreateDirectory(Path.Combine(_directory, name)).FullName;
}
