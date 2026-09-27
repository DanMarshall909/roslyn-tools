using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Win32.SafeHandles;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class JournaledApplyAuthorityTests
{
    [Fact]
    public async Task admitted_replacement_is_physically_staged_and_verified_outside_the_repository()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var durability = new RecordingStagingDurability();

        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot, durability))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);

        var verified = staged.Match(value => value, error => throw new IOException(error.Message));
        verified.Kind.ShouldBe(OperationStagingJournalKind.Verified);
        verified.ObservedHash.ShouldBe(Hash(fixture.Content));
        durability.Flushes.ShouldBe(["directory", "directory", "file", "directory"]);
        File.ReadAllBytes(verified.ArtifactPath).ShouldBe(fixture.Content);
        fixture.LoadOperation(request).StagingJournal.Select(entry => entry.Kind).ShouldBe([
            OperationStagingJournalKind.Intent,
            OperationStagingJournalKind.CreationAdmitted,
            OperationStagingJournalKind.Verified
        ]);
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task symlinked_operation_directory_cannot_redirect_staging_into_the_repository()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Directory.CreateSymbolicLink(Path.Combine(stagingRoot,
            request.OperationId.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            fixture.RepositoryRoot);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);

        staged.IsErr.ShouldBeTrue();
        staged.Match(_ => throw new InvalidOperationException("Expected staging failure."), error => error.Kind)
            .ShouldBe(StagingArtifactErrorKind.ArtifactUnavailable);
        fixture.LoadOperation(request).StagingJournal[^1].Kind
            .ShouldBe(OperationStagingJournalKind.CreationAdmitted);
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsErr.ShouldBeTrue();
        Directory.Exists(Path.Combine(fixture.RepositoryRoot,
            request.OperationId.Nonce.ToString("N"))).ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task failure_after_physical_creation_keeps_pending_admission_for_recovery()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger,
                new ThrowAfterArtifactBoundary(
                    new LinuxStagingArtifactBoundary(stagingRoot), fixture, request))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);

        staged.IsErr.ShouldBeTrue();
        var pending = fixture.LoadOperation(request);
        pending.StagingJournal[^1].Kind.ShouldBe(OperationStagingJournalKind.CreationAdmitted);
        File.ReadAllBytes(pending.StagingJournal[^1].ArtifactPath).ShouldBe(fixture.Content);
        pending.PreEffectActivity.ActiveAdmissions.ShouldContain(activity);
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsErr.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task admitted_artifact_can_be_verified_on_retry_under_the_same_reservation()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        var interrupted = await new JournaledStagingArtifactWriter(
                fixture.Ledger,
                new ThrowAfterArtifactBoundary(
                    new LinuxStagingArtifactBoundary(stagingRoot), fixture, request))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        interrupted.IsErr.ShouldBeTrue();
        var pending = fixture.LoadOperation(request).StagingJournal[^1];
        pending.Kind.ShouldBe(OperationStagingJournalKind.CreationAdmitted);

        var retried = await new JournaledStagingArtifactWriter(
                fixture.CreatePlainLedger(), new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);

        var verified = retried.Match(value => value, error => throw new IOException(error.Message));
        verified.Kind.ShouldBe(OperationStagingJournalKind.Verified);
        verified.CreationToken.ShouldBe(pending.CreationToken);
        verified.ObservedHash.ShouldBe(Hash(fixture.Content));
        fixture.LoadOperation(request).StagingJournal.Select(entry => entry.Kind).ShouldBe([
            OperationStagingJournalKind.Intent,
            OperationStagingJournalKind.CreationAdmitted,
            OperationStagingJournalKind.Verified
        ]);
        File.ReadAllBytes(verified.ArtifactPath).ShouldBe(fixture.Content);
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task verified_artifact_can_be_rechecked_without_another_journal_event()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var first = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        var verified = first.Match(value => value, error => throw new IOException(error.Message));
        var journal = fixture.LoadOperation(request).StagingJournal;

        var retried = await new JournaledStagingArtifactWriter(
                fixture.CreatePlainLedger(), new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);

        retried.Match(value => value, error => throw new IOException(error.Message))
            .ShouldBe(verified);
        fixture.LoadOperation(request).StagingJournal.ShouldBe(journal);
        File.ReadAllBytes(verified.ArtifactPath).ShouldBe(fixture.Content);
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task verified_artifact_recheck_rejects_missing_or_changed_bytes(bool removeArtifact)
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var first = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        var verified = first.Match(value => value, error => throw new IOException(error.Message));
        var journal = fixture.LoadOperation(request).StagingJournal;
        if (removeArtifact)
            File.Delete(verified.ArtifactPath);
        else
            File.WriteAllBytes(verified.ArtifactPath,
                Enumerable.Repeat((byte)'x', fixture.Content.Length).ToArray());

        var retried = await new JournaledStagingArtifactWriter(
                fixture.CreatePlainLedger(), new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);

        retried.IsErr.ShouldBeTrue();
        var failure = retried.Match(
            _ => throw new InvalidOperationException("Expected staging failure."),
            error => error);
        failure.Kind.ShouldBe(StagingArtifactErrorKind.ArtifactUnavailable);
        fixture.LoadOperation(request).StagingJournal.ShouldBe(journal);
        if (removeArtifact)
            File.Exists(verified.ArtifactPath).ShouldBeFalse();
        else
            File.ReadAllBytes(verified.ArtifactPath)
                .ShouldBe(Enumerable.Repeat((byte)'x', fixture.Content.Length).ToArray());
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task verified_artifact_recheck_does_not_recreate_a_missing_directory()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var first = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        var verified = first.Match(value => value, error => throw new IOException(error.Message));
        var journal = fixture.LoadOperation(request).StagingJournal;
        var operationDirectory = Path.GetDirectoryName(verified.ArtifactPath)!;
        Directory.Delete(operationDirectory, recursive: true);

        var retried = await new JournaledStagingArtifactWriter(
                fixture.CreatePlainLedger(), new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);

        retried.IsErr.ShouldBeTrue();
        retried.Match(_ => throw new InvalidOperationException("Expected staging failure."),
            error => error.Kind).ShouldBe(StagingArtifactErrorKind.ArtifactUnavailable);
        Directory.Exists(operationDirectory).ShouldBeFalse();
        fixture.LoadOperation(request).StagingJournal.ShouldBe(journal);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task verified_artifact_can_be_rechecked_after_no_effect_cancellation()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var first = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        var verified = first.Match(value => value, error => throw new IOException(error.Message));
        var journal = fixture.LoadOperation(request).StagingJournal;
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();

        var retried = await new JournaledStagingArtifactWriter(
                fixture.CreatePlainLedger(), new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);

        retried.Match(value => value, error => throw new IOException(error.Message))
            .ShouldBe(verified);
        fixture.LoadOperation(request).StagingJournal.ShouldBe(journal);
        fixture.LoadOperation(request).CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task a_new_reservation_cannot_recheck_a_verified_artifact()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var previousReservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, previousReservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var first = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, previousReservation);
        var verified = first.Match(value => value, error => throw new IOException(error.Message));
        var journal = fixture.LoadOperation(request).StagingJournal;
        previousReservation.Dispose();
        using var newReservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var boundary = new TouchTrackingArtifactBoundary(
            new LinuxStagingArtifactBoundary(stagingRoot));

        var retried = await new JournaledStagingArtifactWriter(
                fixture.CreatePlainLedger(), boundary)
            .StageReplacementAsync(plan, request.OperationId, 1, activity, newReservation);

        retried.IsErr.ShouldBeTrue();
        boundary.Touched.ShouldBeFalse();
        fixture.LoadOperation(request).StagingJournal.ShouldBe(journal);
        File.ReadAllBytes(verified.ArtifactPath).ShouldBe(fixture.Content);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task cancellation_records_the_complete_admitted_artifact_inventory_before_activity_drains()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(twoEffects: true);
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var first = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        first.IsOk.ShouldBeTrue();
        var second = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot,
                    new RecordingStagingDurability(failedFlush: 3)))
            .StageReplacementAsync(plan, request.OperationId, 2, activity, reservation);
        second.IsErr.ShouldBeTrue();
        var beforeCancellation = fixture.LoadOperation(request).StagingJournal;
        beforeCancellation[^1].Kind.ShouldBe(OperationStagingJournalKind.CreationAdmitted);

        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();

        var cancelled = fixture.LoadOperation(request);
        cancelled.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        cancelled.CleanupJournal.ShouldHaveSingleItem();
        var pending = cancelled.CleanupJournal[0];
        pending.Kind.ShouldBe(OperationCleanupJournalKind.InventoryPending);
        pending.Artifacts.Select(artifact => artifact.EffectSequence).ShouldBe([1, 2]);
        pending.Artifacts.Select(artifact => artifact.ArtifactPath).ShouldBe(
            beforeCancellation.Where(entry => entry.Kind == OperationStagingJournalKind.CreationAdmitted)
                .Select(entry => entry.ArtifactPath));
        pending.Artifacts.Select(artifact => artifact.CreationToken).ShouldBe(
            beforeCancellation.Where(entry => entry.Kind == OperationStagingJournalKind.CreationAdmitted)
                .Select(entry => entry.CreationToken!.Value));
        cancelled.PreEffectActivity.ActiveAdmissions.ShouldContain(activity);
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsErr.ShouldBeTrue();

        var retried = await new JournaledStagingArtifactWriter(
                fixture.CreatePlainLedger(), new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 2, activity, reservation);
        retried.IsOk.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        var drained = fixture.LoadOperation(request);
        drained.CleanupJournal.ShouldHaveSingleItem();
        drained.CleanupJournal[0].Sequence.ShouldBe(pending.Sequence);
        drained.CleanupJournal[0].Kind.ShouldBe(pending.Kind);
        drained.CleanupJournal[0].CommittedAtUtc.ShouldBe(pending.CommittedAtUtc);
        drained.CleanupJournal[0].Artifacts.ToArray().ShouldBe(pending.Artifacts.ToArray());
        drained.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        fixture.ReloadPlan().Pins.Any(pin =>
            pin.Kind == ChangePlanPinKind.OperationLedger
            && pin.ReferenceId == DurableDispatchIntakeCoordinator.FormatOperationReference(
                request.OperationId)).ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
        File.Exists(Path.Combine(fixture.RepositoryRoot, "src", "Added2.cs")).ShouldBeFalse();

        new JournaledNoEffectCleanup(
                fixture.Ledger, fixture.Store,
                new LinuxStagingCleanupBoundary(stagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();
        fixture.LoadOperation(request).CleanupJournal[^1].Kind
            .ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
        File.Exists(first.Match(value => value, _ => null!).ArtifactPath).ShouldBeFalse();
        File.Exists(retried.Match(value => value, _ => null!).ArtifactPath).ShouldBeFalse();
    }

    [Theory]
    [InlineData("CancelledNoEffect")]
    [InlineData("RejectedStale")]
    [InlineData("RejectedPlanUnavailable")]
    [InlineData("RejectedUnsafeCoordination")]
    [InlineData("Expired")]
    public async Task drained_no_effect_outcome_records_pending_artifact_cleanup(
        string outcomeName)
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var outcome = Enum.Parse<OperationLifecycleState>(outcomeName);
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        var verified = staged.Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();

        fixture.Ledger.AppendTransition(request.RepositoryId, request.OperationId,
                OperationLifecycleState.Prepared, outcome, "No repository effect admitted.")
            .IsOk.ShouldBeTrue();

        var terminal = fixture.LoadOperation(request);
        terminal.CurrentState.ShouldBe(outcome);
        terminal.PreEffectActivity.IsClosed.ShouldBeTrue();
        terminal.CleanupJournal.ShouldHaveSingleItem();
        terminal.CleanupJournal[0].Kind.ShouldBe(OperationCleanupJournalKind.InventoryPending);
        terminal.CleanupJournal[0].Artifacts.ShouldHaveSingleItem();
        terminal.CleanupJournal[0].Artifacts[0].ArtifactPath.ShouldBe(verified.ArtifactPath);
        terminal.CleanupJournal[0].Artifacts[0].ExpectedHash.ShouldBe(Hash(fixture.Content));
        terminal.CleanupJournal[0].Artifacts[0].CreationToken.ShouldBe(verified.CreationToken!.Value);
        File.Exists(verified.ArtifactPath).ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task cancellation_with_only_staging_intent_keeps_empty_cleanup_pending()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendStagingIntent(
                plan, request.OperationId, 1, activity, reservation)
            .IsOk.ShouldBeTrue();

        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();

        var cancelled = fixture.LoadOperation(request);
        cancelled.CleanupJournal.ShouldHaveSingleItem();
        cancelled.CleanupJournal[0].Kind.ShouldBe(OperationCleanupJournalKind.InventoryPending);
        cancelled.CleanupJournal[0].Artifacts.ShouldBeEmpty();
        cancelled.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        Directory.Exists(plan.Envelope.Payload.Coordination.StagingRoot).ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task no_effect_cleanup_waits_for_drain_then_removes_artifact_and_releases_pin()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        var artifact = staged.Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();
        var cleanup = new JournaledNoEffectCleanup(
            fixture.Ledger, fixture.Store, new LinuxStagingCleanupBoundary(stagingRoot));

        cleanup.Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsErr.ShouldBeTrue();
        File.Exists(artifact.ArtifactPath).ShouldBeTrue();
        fixture.LoadOperation(request).CleanupJournal.ShouldHaveSingleItem();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();

        var cleaned = cleanup.Cleanup(request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        cleaned.CleanupJournal.Select(entry => entry.Kind).ShouldBe([
            OperationCleanupJournalKind.InventoryPending,
            OperationCleanupJournalKind.ArtifactRemoved,
            OperationCleanupJournalKind.CleanupVerified
        ]);
        File.Exists(artifact.ArtifactPath).ShouldBeFalse();
        Directory.Exists(Path.GetDirectoryName(artifact.ArtifactPath)!).ShouldBeFalse();
        fixture.ReloadPlan().Pins.Any(pin =>
            pin.Kind == ChangePlanPinKind.OperationLedger
            && pin.ReferenceId == DurableDispatchIntakeCoordinator.FormatOperationReference(
                request.OperationId)).ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();

        var replay = new JournaledNoEffectCleanup(
            fixture.CreatePlainLedger(), fixture.Store, new LinuxStagingCleanupBoundary(stagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        replay.CleanupJournal.Select(entry => entry.Sequence)
            .ShouldBe(cleaned.CleanupJournal.Select(entry => entry.Sequence));
        replay.CleanupJournal.Select(entry => entry.Kind)
            .ShouldBe(cleaned.CleanupJournal.Select(entry => entry.Kind));
        replay.CleanupJournal.SelectMany(entry => entry.Artifacts)
            .ShouldBe(cleaned.CleanupJournal.SelectMany(entry => entry.Artifacts));
    }

    [Fact]
    public async Task uncertain_cleanup_verification_does_not_release_the_plan_pin()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        staged.IsOk.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();
        var failing = new JournaledNoEffectCleanup(
            fixture.CreateLedger(new FailDirectoryFlushFrom(2)),
            fixture.Store,
            new LinuxStagingCleanupBoundary(stagingRoot));

        failing.Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).CleanupJournal[^1].Kind
            .ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        failing.Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsErr.ShouldBeTrue();
        fixture.ReloadPlan().Pins.Any(pin =>
            pin.Kind == ChangePlanPinKind.OperationLedger
            && pin.ReferenceId == DurableDispatchIntakeCoordinator.FormatOperationReference(
                request.OperationId)).ShouldBeTrue();

        new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.Store,
                new LinuxStagingCleanupBoundary(stagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();
        fixture.ReloadPlan().Pins.Any(pin =>
            pin.Kind == ChangePlanPinKind.OperationLedger
            && pin.ReferenceId == DurableDispatchIntakeCoordinator.FormatOperationReference(
                request.OperationId)).ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task cleanup_leaves_unknown_operation_root_entries_and_pin_pending()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        var artifact = staged.Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();
        var unknown = Path.Combine(Path.GetDirectoryName(artifact.ArtifactPath)!, "unexpected");
        File.WriteAllText(unknown, "not admitted");
        var cleanup = new JournaledNoEffectCleanup(
            fixture.Ledger, fixture.Store, new LinuxStagingCleanupBoundary(stagingRoot));

        cleanup.Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsErr.ShouldBeTrue();

        fixture.LoadOperation(request).CleanupJournal.Select(entry => entry.Kind).ShouldBe([
            OperationCleanupJournalKind.InventoryPending,
            OperationCleanupJournalKind.ArtifactRemoved
        ]);
        File.Exists(artifact.ArtifactPath).ShouldBeFalse();
        File.Exists(unknown).ShouldBeTrue();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();

        File.Delete(unknown);
        cleanup.Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();
        Directory.Exists(Path.GetDirectoryName(artifact.ArtifactPath)!).ShouldBeFalse();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task interrupted_artifact_unlink_retries_without_claiming_premature_cleanup()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        var artifact = staged.Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();
        var durability = new RecordingStagingDurability(failedFlush: 3);
        var interrupted = new JournaledNoEffectCleanup(
            fixture.Ledger, fixture.Store,
            new LinuxStagingCleanupBoundary(stagingRoot, durability));

        interrupted.Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsErr.ShouldBeTrue();

        File.Exists(artifact.ArtifactPath).ShouldBeFalse();
        fixture.LoadOperation(request).CleanupJournal.ShouldHaveSingleItem();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.Store,
                new LinuxStagingCleanupBoundary(stagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();
        fixture.LoadOperation(request).CleanupJournal[^1].Kind
            .ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task intent_only_cleanup_verifies_an_absent_operation_root_without_creating_it()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 1, activity, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();

        var uncertainAbsence = new RecordingStagingDurability(failedFlush: 1);
        new JournaledNoEffectCleanup(
                fixture.Ledger, fixture.Store,
                new LinuxStagingCleanupBoundary(
                    plan.Envelope.Payload.Coordination.StagingRoot, uncertainAbsence))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsErr.ShouldBeTrue();
        uncertainAbsence.Flushes.ShouldBe(["directory"]);
        fixture.LoadOperation(request).CleanupJournal.ShouldHaveSingleItem();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();

        new JournaledNoEffectCleanup(
                fixture.Ledger, fixture.Store,
                new LinuxStagingCleanupBoundary(
                    plan.Envelope.Payload.Coordination.StagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();

        fixture.LoadOperation(request).CleanupJournal.Select(entry => entry.Kind).ShouldBe([
            OperationCleanupJournalKind.InventoryPending,
            OperationCleanupJournalKind.CleanupVerified
        ]);
        Directory.Exists(plan.Envelope.Payload.Coordination.StagingRoot).ShouldBeFalse();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Theory]
    [InlineData("changed-bytes")]
    [InlineData("symlink")]
    [InlineData("hard-link")]
    public async Task cleanup_rejects_a_changed_redirected_or_linked_admitted_artifact(string alteration)
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        var artifact = staged.Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();
        var important = Path.Combine(fixture.RepositoryRoot, "src", "Important.cs");
        File.WriteAllText(important, "class Important;\n");
        if (alteration == "changed-bytes")
            File.WriteAllText(artifact.ArtifactPath, "changed\n");
        else if (alteration == "symlink")
        {
            File.Delete(artifact.ArtifactPath);
            File.CreateSymbolicLink(artifact.ArtifactPath, important);
        }
        else
        {
            Link(artifact.ArtifactPath, Path.Combine(stagingRoot, "artifact-alias"))
                .ShouldBe(0);
        }

        new JournaledNoEffectCleanup(
                fixture.Ledger, fixture.Store,
                new LinuxStagingCleanupBoundary(stagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsErr.ShouldBeTrue();

        fixture.LoadOperation(request).CleanupJournal.ShouldHaveSingleItem();
        File.Exists(artifact.ArtifactPath).ShouldBeTrue();
        File.ReadAllText(important).ShouldBe("class Important;\n");
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task interrupted_empty_root_removal_retries_before_cleanup_verification()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        var artifact = staged.Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();
        var durability = new RecordingStagingDurability(failedFlush: 6);

        new JournaledNoEffectCleanup(
                fixture.Ledger, fixture.Store,
                new LinuxStagingCleanupBoundary(stagingRoot, durability))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsErr.ShouldBeTrue();

        durability.Flushes.Count.ShouldBe(6);
        File.Exists(artifact.ArtifactPath).ShouldBeFalse();
        Directory.Exists(Path.GetDirectoryName(artifact.ArtifactPath)!).ShouldBeFalse();
        fixture.LoadOperation(request).CleanupJournal.Select(entry => entry.Kind).ShouldBe([
            OperationCleanupJournalKind.InventoryPending,
            OperationCleanupJournalKind.ArtifactRemoved
        ]);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();

        Directory.Delete(stagingRoot, recursive: true);
        var missingRootDurability = new RecordingStagingDurability(failedFlush: 1);
        new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.Store,
                new LinuxStagingCleanupBoundary(stagingRoot, missingRootDurability))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsErr.ShouldBeTrue();
        missingRootDurability.Flushes.ShouldBe(["directory"]);
        fixture.LoadOperation(request).CleanupJournal.Select(entry => entry.Kind).ShouldBe([
            OperationCleanupJournalKind.InventoryPending,
            OperationCleanupJournalKind.ArtifactRemoved
        ]);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();

        new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.Store,
                new LinuxStagingCleanupBoundary(stagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();
        fixture.LoadOperation(request).CleanupJournal[^1].Kind
            .ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task cleanup_removes_all_admitted_artifacts_in_journal_order()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(twoEffects: true);
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var writer = new JournaledStagingArtifactWriter(
            fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot));
        var first = await writer.StageReplacementAsync(
            plan, request.OperationId, 1, activity, reservation);
        var second = await writer.StageReplacementAsync(
            plan, request.OperationId, 2, activity, reservation);
        first.IsOk.ShouldBeTrue();
        second.IsOk.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();

        new JournaledNoEffectCleanup(
                fixture.Ledger, fixture.Store,
                new LinuxStagingCleanupBoundary(stagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();

        fixture.LoadOperation(request).CleanupJournal.Select(entry => entry.Kind).ShouldBe([
            OperationCleanupJournalKind.InventoryPending,
            OperationCleanupJournalKind.ArtifactRemoved,
            OperationCleanupJournalKind.ArtifactRemoved,
            OperationCleanupJournalKind.CleanupVerified
        ]);
        fixture.LoadOperation(request).CleanupJournal
            .Where(entry => entry.Kind == OperationCleanupJournalKind.ArtifactRemoved)
            .SelectMany(entry => entry.Artifacts)
            .Select(artifact => artifact.EffectSequence).ShouldBe([1, 2]);
        File.Exists(first.Match(value => value, _ => null!).ArtifactPath).ShouldBeFalse();
        File.Exists(second.Match(value => value, _ => null!).ArtifactPath).ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
        File.Exists(Path.Combine(fixture.RepositoryRoot, "src", "Added2.cs")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("missing-removal")]
    [InlineData("wrong-artifact")]
    public async Task restart_rejects_changed_cleanup_progress(string tampering)
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        staged.IsOk.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();
        new JournaledNoEffectCleanup(
                fixture.Ledger, fixture.Store,
                new LinuxStagingCleanupBoundary(stagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();
        var recordPath = DurableOperationLedger.GetRecordPath(
            fixture.LedgerRoot, request.RepositoryId, request.OperationId);
        var json = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        var cleanup = json["cleanupJournal"]!.AsArray();
        if (tampering == "missing-removal")
        {
            cleanup.RemoveAt(1);
            cleanup[1]!["sequence"] = 2;
        }
        else
        {
            cleanup[1]!["artifacts"]!.AsArray()[0]!["artifactPath"] = fixture.TargetPath;
        }
        File.WriteAllText(recordPath, json.ToJsonString());

        fixture.CreatePlainLedger().Load(request.RepositoryId, request.OperationId)
            .Match(_ => throw new ShouldAssertException("Changed cleanup progress was accepted."),
                error => error.Kind)
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task verified_cleanup_replays_after_the_unpinned_plan_is_collected()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        staged.IsOk.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();
        var cleanup = new JournaledNoEffectCleanup(
            fixture.Ledger, fixture.Store, new LinuxStagingCleanupBoundary(stagingRoot));
        cleanup.Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Now = fixture.Now.AddDays(2);
        fixture.Store.CollectGarbage().DeletedPlans.ShouldBe(1);

        cleanup.Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();
        fixture.LoadOperation(request).CleanupJournal[^1].Kind
            .ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Theory]
    [InlineData("RejectedStale")]
    [InlineData("RejectedPlanUnavailable")]
    [InlineData("RejectedUnsafeCoordination")]
    [InlineData("Expired")]
    public async Task drained_no_effect_outcomes_use_the_same_verified_cleanup_path(
        string outcomeName)
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        var artifact = staged.Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        var outcome = Enum.Parse<OperationLifecycleState>(outcomeName);
        fixture.Ledger.AppendTransition(request.RepositoryId, request.OperationId,
                OperationLifecycleState.Prepared, outcome, "No repository effect admitted.")
            .IsOk.ShouldBeTrue();

        new JournaledNoEffectCleanup(
                fixture.Ledger, fixture.Store,
                new LinuxStagingCleanupBoundary(stagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();

        fixture.LoadOperation(request).CurrentState.ShouldBe(outcome);
        fixture.LoadOperation(request).CleanupJournal[^1].Kind
            .ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        File.Exists(artifact.ArtifactPath).ShouldBeFalse();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task a_new_reservation_cannot_inherit_pending_physical_cleanup()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string artifactPath;
        using (var original = await fixture.Reservations.AcquireAsync(
                   plan.Envelope.Payload.Repository, CancellationToken.None))
        {
            var activity = fixture.Ledger.BeginPreEffectActivity(
                    request.RepositoryId, request.OperationId, original)
                .Match(value => value, error => throw new IOException(error.Message));
            var staged = await new JournaledStagingArtifactWriter(
                    fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
                .StageReplacementAsync(plan, request.OperationId, 1, activity, original);
            artifactPath = staged.Match(value => value, error => throw new IOException(error.Message))
                .ArtifactPath;
            fixture.Ledger.CompletePreEffectActivity(
                    request.RepositoryId, request.OperationId, activity, original)
                .IsOk.ShouldBeTrue();
            fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
                .IsOk.ShouldBeTrue();
        }
        using var replacement = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);

        new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.Store,
                new LinuxStagingCleanupBoundary(stagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, replacement)
            .IsErr.ShouldBeTrue();

        File.Exists(artifactPath).ShouldBeTrue();
        fixture.LoadOperation(request).CleanupJournal.ShouldHaveSingleItem();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task cleanup_cannot_remove_artifacts_admitted_by_an_earlier_reservation()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(twoEffects: true);
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var writer = new JournaledStagingArtifactWriter(
            fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot));
        string firstPath;
        using (var firstReservation = await fixture.Reservations.AcquireAsync(
                   plan.Envelope.Payload.Repository, CancellationToken.None))
        {
            var firstActivity = fixture.Ledger.BeginPreEffectActivity(
                    request.RepositoryId, request.OperationId, firstReservation)
                .Match(value => value, error => throw new IOException(error.Message));
            firstPath = (await writer.StageReplacementAsync(
                    plan, request.OperationId, 1, firstActivity, firstReservation))
                .Match(value => value.ArtifactPath, error => throw new IOException(error.Message));
            fixture.Ledger.CompletePreEffectActivity(
                    request.RepositoryId, request.OperationId, firstActivity, firstReservation)
                .IsOk.ShouldBeTrue();
        }

        using var secondReservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var secondActivity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, secondReservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var secondPath = (await writer.StageReplacementAsync(
                plan, request.OperationId, 2, secondActivity, secondReservation))
            .Match(value => value.ArtifactPath, error => throw new IOException(error.Message));
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, secondActivity, secondReservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();

        new JournaledNoEffectCleanup(
                fixture.Ledger, fixture.Store,
                new LinuxStagingCleanupBoundary(stagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, secondReservation)
            .IsErr.ShouldBeTrue();

        File.Exists(firstPath).ShouldBeTrue();
        File.Exists(secondPath).ShouldBeTrue();
        fixture.LoadOperation(request).CleanupJournal.ShouldHaveSingleItem();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
        File.Exists(Path.Combine(fixture.RepositoryRoot, "src", "Added2.cs")).ShouldBeFalse();
    }

    [Fact]
    public async Task a_fresh_creation_does_not_adopt_a_preexisting_matching_artifact()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        var epochDirectory = Path.Combine(stagingRoot,
            request.OperationId.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var operationDirectory = Path.Combine(epochDirectory, request.OperationId.Nonce.ToString("N"));
        Directory.CreateDirectory(operationDirectory);
        foreach (var path in new[] { stagingRoot, epochDirectory, operationDirectory })
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var artifactPath = Path.Combine(operationDirectory, "00000001.stage");
        File.WriteAllBytes(artifactPath, fixture.Content);
        File.SetUnixFileMode(artifactPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);

        staged.IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).StagingJournal[^1].Kind
            .ShouldBe(OperationStagingJournalKind.CreationAdmitted);
        File.ReadAllBytes(artifactPath).ShouldBe(fixture.Content);
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsErr.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Theory]
    [InlineData("different-bytes")]
    [InlineData("short-content")]
    [InlineData("hard-link")]
    [InlineData("symbolic-link")]
    [InlineData("public-permissions")]
    public async Task retry_rejects_an_unexpected_existing_artifact_without_overwriting_it(
        string artifactState)
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var interrupted = await new JournaledStagingArtifactWriter(
                fixture.Ledger,
                new ThrowAfterArtifactBoundary(
                    new LinuxStagingArtifactBoundary(stagingRoot), fixture, request))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        interrupted.IsErr.ShouldBeTrue();
        var pending = fixture.LoadOperation(request).StagingJournal[^1];
        var repositorySource = Path.Combine(fixture.RepositoryRoot, "src", "Original.cs");
        if (artifactState is "hard-link" or "symbolic-link")
        {
            File.WriteAllBytes(repositorySource, fixture.Content);
            File.SetUnixFileMode(repositorySource, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Delete(pending.ArtifactPath);
        }
        switch (artifactState)
        {
            case "different-bytes":
                File.WriteAllText(pending.ArtifactPath, "class Other;\n");
                break;
            case "short-content":
                File.WriteAllText(pending.ArtifactPath, "short");
                break;
            case "hard-link":
                Link(repositorySource, pending.ArtifactPath).ShouldBe(0);
                break;
            case "symbolic-link":
                File.CreateSymbolicLink(pending.ArtifactPath, repositorySource);
                break;
            case "public-permissions":
                File.SetUnixFileMode(pending.ArtifactPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite
                    | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(artifactState));
        }

        var retried = await new JournaledStagingArtifactWriter(
                fixture.CreatePlainLedger(), new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);

        retried.IsErr.ShouldBeTrue();
        var failure = retried.Match(
            _ => throw new InvalidOperationException("Expected staging failure."),
            error => error);
        failure.Kind.ShouldBe(StagingArtifactErrorKind.ArtifactUnavailable);
        if (artifactState == "short-content")
            failure.Cause!.Message.ShouldContain("byte length");
        if (artifactState == "hard-link")
            failure.Cause!.Message.ShouldContain("singly linked");
        if (artifactState == "public-permissions")
            failure.Cause!.Message.ShouldContain("private");
        fixture.LoadOperation(request).StagingJournal[^1].Kind
            .ShouldBe(OperationStagingJournalKind.CreationAdmitted);
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsErr.ShouldBeTrue();
        if (artifactState is "different-bytes" or "short-content")
            File.ReadAllText(pending.ArtifactPath).ShouldBe(
                artifactState == "different-bytes" ? "class Other;\n" : "short");
        else if (artifactState == "public-permissions")
            File.ReadAllBytes(pending.ArtifactPath).ShouldBe(fixture.Content);
        else
            File.ReadAllBytes(repositorySource).ShouldBe(fixture.Content);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task retry_rejects_a_fifo_without_waiting_for_a_writer()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var interrupted = await new JournaledStagingArtifactWriter(
                fixture.Ledger,
                new ThrowAfterArtifactBoundary(
                    new LinuxStagingArtifactBoundary(stagingRoot), fixture, request))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        interrupted.IsErr.ShouldBeTrue();
        var pending = fixture.LoadOperation(request).StagingJournal[^1];
        File.Delete(pending.ArtifactPath);
        MkFifo(pending.ArtifactPath, Convert.ToUInt32("600", 8)).ShouldBe(0);

        using var started = new ManualResetEventSlim();
        var retryTask = Task.Run(async () =>
        {
            started.Set();
            return await new JournaledStagingArtifactWriter(
                    fixture.CreatePlainLedger(), new LinuxStagingArtifactBoundary(stagingRoot))
                .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        });
        started.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
        var finishedWithoutWriter = await Task.WhenAny(
            retryTask, Task.Delay(TimeSpan.FromSeconds(3))) == retryTask;
        if (!finishedWithoutWriter)
        {
            using var unblock = new FileStream(pending.ArtifactPath, FileMode.Open,
                FileAccess.ReadWrite, FileShare.ReadWrite);
            await retryTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        finishedWithoutWriter.ShouldBeTrue();
        var retry = await retryTask;
        retry.IsErr.ShouldBeTrue();
        var failure = retry.Match(
            _ => throw new InvalidOperationException("Expected staging failure."),
            error => error);
        failure.Kind.ShouldBe(StagingArtifactErrorKind.ArtifactUnavailable);
        failure.Cause!.Message.ShouldContain("singly linked regular file");
        fixture.LoadOperation(request).StagingJournal[^1].Kind
            .ShouldBe(OperationStagingJournalKind.CreationAdmitted);
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsErr.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task admitted_artifact_retry_can_finish_after_no_effect_cancellation()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var interrupted = await new JournaledStagingArtifactWriter(
                fixture.Ledger,
                new ThrowAfterArtifactBoundary(
                    new LinuxStagingArtifactBoundary(stagingRoot), fixture, request))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        interrupted.IsErr.ShouldBeTrue();
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();

        var retried = await new JournaledStagingArtifactWriter(
                fixture.CreatePlainLedger(), new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);

        var verified = retried.Match(value => value, error => throw new IOException(error.Message));
        verified.Kind.ShouldBe(OperationStagingJournalKind.Verified);
        fixture.LoadOperation(request).CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task admitted_creation_can_make_a_missing_artifact_after_no_effect_cancellation()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        var interrupted = await new JournaledStagingArtifactWriter(
                fixture.Ledger,
                new LinuxStagingArtifactBoundary(stagingRoot,
                    new RecordingStagingDurability(failedFlush: 1)))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);
        interrupted.IsErr.ShouldBeTrue();
        var pending = fixture.LoadOperation(request).StagingJournal[^1];
        pending.Kind.ShouldBe(OperationStagingJournalKind.CreationAdmitted);
        File.Exists(pending.ArtifactPath).ShouldBeFalse();
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();

        var retried = await new JournaledStagingArtifactWriter(
                fixture.CreatePlainLedger(), new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);

        var verified = retried.Match(value => value, error => throw new IOException(error.Message));
        verified.Kind.ShouldBe(OperationStagingJournalKind.Verified);
        verified.CreationToken.ShouldBe(pending.CreationToken);
        File.ReadAllBytes(verified.ArtifactPath).ShouldBe(fixture.Content);
        fixture.LoadOperation(request).CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task a_new_reservation_cannot_take_over_an_admitted_artifact_retry()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var previousReservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, previousReservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var interrupted = await new JournaledStagingArtifactWriter(
                fixture.Ledger,
                new ThrowAfterArtifactBoundary(
                    new LinuxStagingArtifactBoundary(stagingRoot), fixture, request))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, previousReservation);
        interrupted.IsErr.ShouldBeTrue();
        var pending = fixture.LoadOperation(request).StagingJournal;
        previousReservation.Dispose();
        using var newReservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        newReservation.FenceEpoch.ShouldBeGreaterThan(activity.FenceEpoch);

        var boundary = new TouchTrackingArtifactBoundary(
            new LinuxStagingArtifactBoundary(stagingRoot));
        var retried = await new JournaledStagingArtifactWriter(
                fixture.CreatePlainLedger(), boundary)
            .StageReplacementAsync(plan, request.OperationId, 1, activity, newReservation);

        retried.IsErr.ShouldBeTrue();
        boundary.Touched.ShouldBeFalse();
        fixture.LoadOperation(request).StagingJournal.ShouldBe(pending);
        File.ReadAllBytes(pending[^1].ArtifactPath).ShouldBe(fixture.Content);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(2, true, false)]
    [InlineData(3, true, true)]
    [InlineData(4, true, true)]
    public async Task flush_failure_keeps_creation_pending_without_repository_effect(
        int failedFlush,
        bool operationDirectoryExists,
        bool artifactExists)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var durability = new RecordingStagingDurability(failedFlush);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot, durability))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);

        staged.IsErr.ShouldBeTrue();
        var pending = fixture.LoadOperation(request);
        pending.StagingJournal[^1].Kind.ShouldBe(OperationStagingJournalKind.CreationAdmitted);
        durability.Flushes.Count.ShouldBe(failedFlush);
        Directory.Exists(Path.GetDirectoryName(pending.StagingJournal[^1].ArtifactPath)!)
            .ShouldBe(operationDirectoryExists);
        File.Exists(pending.StagingJournal[^1].ArtifactPath).ShouldBe(artifactExists);
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsErr.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task reused_epoch_directory_is_flushed_before_artifact_creation()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        var epochDirectory = Path.Combine(stagingRoot,
            request.OperationId.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(epochDirectory);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(epochDirectory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var durability = new RecordingStagingDurability(failedFlush: 1);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot, durability))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);

        staged.IsErr.ShouldBeTrue();
        durability.Flushes.ShouldBe(["directory"]);
        fixture.LoadOperation(request).StagingJournal[^1].Kind
            .ShouldBe(OperationStagingJournalKind.CreationAdmitted);
        Directory.Exists(Path.Combine(epochDirectory, request.OperationId.Nonce.ToString("N")))
            .ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task unavailable_staging_root_fails_before_durable_artifact_intent()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);

        staged.IsErr.ShouldBeTrue();
        staged.Match(_ => throw new InvalidOperationException("Expected staging failure."),
                error => error.Kind)
            .ShouldBe(StagingArtifactErrorKind.UnsafeRoot);
        fixture.LoadOperation(request).StagingJournal.ShouldBeEmpty();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        Directory.Exists(stagingRoot).ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task symlinked_staging_root_fails_before_durable_artifact_intent()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateSymbolicLink(stagingRoot, fixture.RepositoryRoot);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);

        staged.IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).StagingJournal.ShouldBeEmpty();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task group_writable_staging_root_fails_before_durable_artifact_intent()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute | UnixFileMode.GroupWrite);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);

        staged.IsErr.ShouldBeTrue();
        staged.Match(_ => throw new InvalidOperationException("Expected staging failure."),
                error => error.Kind)
            .ShouldBe(StagingArtifactErrorKind.UnsafeRoot);
        fixture.LoadOperation(request).StagingJournal.ShouldBeEmpty();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task staging_on_a_different_mount_fails_before_durable_artifact_intent()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64
            || !Directory.Exists("/dev/shm"))
            return;

        using var fixture = new ApplyFixture(differentMountControlRoot: true);
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        Directory.CreateDirectory(stagingRoot);
        File.SetUnixFileMode(stagingRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var staging = LinuxStagingHandles.OpenAbsoluteDirectory(stagingRoot);
        using var repository = LinuxStagingHandles.OpenAbsoluteDirectory(fixture.RepositoryRoot);
        LinuxStagingHandles.Identity(staging).MountId
            .ShouldNotBe(LinuxStagingHandles.Identity(repository).MountId);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation);

        staged.IsErr.ShouldBeTrue();
        staged.Match(_ => throw new InvalidOperationException("Expected staging failure."),
                error => error.Kind)
            .ShouldBe(StagingArtifactErrorKind.UnsafeRoot);
        fixture.LoadOperation(request).StagingJournal.ShouldBeEmpty();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task exact_staging_intent_is_durable_before_artifact_work_and_verification_survives_restart()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var admission = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        var intent = fixture.Ledger.AppendStagingIntent(
                plan, request.OperationId, 1, admission, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var restarted = fixture.LoadOperation(request);

        intent.StagingJournal.ShouldHaveSingleItem();
        restarted.StagingJournal.ShouldBe(intent.StagingJournal);
        restarted.StagingJournal[0].Kind.ShouldBe(OperationStagingJournalKind.Intent);
        restarted.StagingJournal[0].ExpectedHash.ShouldBe(Hash(fixture.Content));
        restarted.StagingJournal[0].ArtifactPath.ShouldBe(Path.Combine(
            plan.Envelope.Payload.Coordination.StagingRoot,
            request.OperationId.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.OperationId.Nonce.ToString("N"),
            "00000001.stage"));
        Directory.Exists(plan.Envelope.Payload.Coordination.StagingRoot).ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();

        var creation = fixture.Ledger.AppendStagingCreationAdmission(
                plan, request.OperationId, 1, admission, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        creation.CreationToken.ShouldNotBeNull();
        Directory.Exists(plan.Envelope.Payload.Coordination.StagingRoot).ShouldBeFalse();

        var verified = fixture.Ledger.AppendStagingVerification(
                plan, request.OperationId, 1, Hash(fixture.Content), creation.CreationToken.Value,
                admission, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        verified.StagingJournal.Select(entry => entry.Kind).ShouldBe([
            OperationStagingJournalKind.Intent,
            OperationStagingJournalKind.CreationAdmitted,
            OperationStagingJournalKind.Verified
        ]);
        fixture.LoadOperation(request).StagingJournal.ShouldBe(verified.StagingJournal);
        fixture.Ledger.AppendStagingVerification(
                plan, request.OperationId, 1, Hash(fixture.Content), creation.CreationToken.Value,
                admission, reservation)
            .Match(value => value, error => throw new IOException(error.Message))
            .StagingJournal.ShouldBe(verified.StagingJournal);
    }

    [Fact]
    public async Task cancelled_generation_retains_staging_intent_but_cannot_verify_or_add_artifacts()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var admission = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 1, admission, reservation)
            .IsOk.ShouldBeTrue();

        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();

        fixture.Ledger.AppendStagingCreationAdmission(
                plan, request.OperationId, 1, admission, reservation)
            .IsErr.ShouldBeTrue();
        fixture.Ledger.AppendStagingVerification(
                plan, request.OperationId, 1, Hash(fixture.Content), Guid.NewGuid(),
                admission, reservation)
            .IsErr.ShouldBeTrue();
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 1, admission, reservation)
            .IsErr.ShouldBeTrue();
        var retained = fixture.LoadOperation(request);
        retained.StagingJournal.ShouldHaveSingleItem();
        retained.StagingJournal[0].Kind.ShouldBe(OperationStagingJournalKind.Intent);
        retained.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task an_admitted_staging_creation_can_finish_after_cancellation_then_drain()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 1, activity, reservation)
            .IsOk.ShouldBeTrue();
        var creation = fixture.Ledger.AppendStagingCreationAdmission(
                plan, request.OperationId, 1, activity, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();

        fixture.Ledger.AppendStagingVerification(
                plan, request.OperationId, 1, Hash(fixture.Content), Guid.NewGuid(),
                activity, reservation)
            .IsErr.ShouldBeTrue();
        fixture.Ledger.AppendStagingVerification(
                plan, request.OperationId, 1, Hash(fixture.Content), creation.CreationToken!.Value,
                activity, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.AppendStagingCreationAdmission(
                plan, request.OperationId, 1, activity, reservation)
            .IsErr.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        var restarted = fixture.LoadOperation(request);
        restarted.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        restarted.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        restarted.StagingJournal.Select(entry => entry.Kind).ShouldBe([
            OperationStagingJournalKind.Intent,
            OperationStagingJournalKind.CreationAdmitted,
            OperationStagingJournalKind.Verified
        ]);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task unresolved_staging_creation_cannot_drain_or_clear_the_repository_barrier()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 1, activity, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.AppendStagingCreationAdmission(
                plan, request.OperationId, 1, activity, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();

        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsErr.ShouldBeTrue();
        fixture.Ledger.ConfirmDurableCancelledNoEffect(request.RepositoryId, request.OperationId)
            .IsErr.ShouldBeTrue();
        var nextRequest = fixture.Prepare();
        fixture.Ledger.HasUnresolvedRepositoryMutation(
                request.RepositoryId, nextRequest.OperationId)
            .Match(value => value, error => throw new IOException(error.Message))
            .ShouldBeTrue();
        fixture.LoadOperation(request).StagingJournal[^1].Kind
            .ShouldBe(OperationStagingJournalKind.CreationAdmitted);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task first_repository_effect_cannot_start_while_staging_intent_is_unverified()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var admission = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 1, admission, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, admission, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.ClaimApplyOwner(
                request.RepositoryId, request.OperationId, reservation, plan.Key.PlanHash)
            .IsOk.ShouldBeTrue();

        var intent = fixture.Ledger.AppendEffectIntent(
            request.RepositoryId,
            request.OperationId,
            1,
            new string('a', 64),
            reservation,
            CanonicalChangePlan.ComputeEffectHash(plan.Envelope.Payload.Effects[0]),
            OperationGuardTokenSet.Expected(plan.Envelope.Payload));

        intent.IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).EffectJournal.ShouldBeEmpty();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task staging_rejects_wrong_plan_admission_and_digest_without_changing_journal()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var admission = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var wrongPlan = plan with { Key = plan.Key with { PlanHash = new string('b', 64) } };

        fixture.Ledger.AppendStagingIntent(
                wrongPlan, request.OperationId, 1, admission, reservation)
            .IsErr.ShouldBeTrue();
        fixture.Ledger.AppendStagingIntent(
                plan, request.OperationId, 1, admission with { Token = Guid.NewGuid() }, reservation)
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).StagingJournal.ShouldBeEmpty();
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 1, admission, reservation)
            .IsOk.ShouldBeTrue();
        var creation = fixture.Ledger.AppendStagingCreationAdmission(
                plan, request.OperationId, 1, admission, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var original = fixture.LoadOperation(request).StagingJournal;

        fixture.Ledger.AppendStagingVerification(
                plan, request.OperationId, 1, new string('c', 64), creation.CreationToken!.Value,
                admission, reservation)
            .IsErr.ShouldBeTrue();

        fixture.LoadOperation(request).StagingJournal.ShouldBe(original);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task visible_staging_intent_after_directory_flush_failure_must_be_reconfirmed()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var admission = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var failing = fixture.CreateLedger(new FailNthDirectoryFlush(1));

        failing.AppendStagingIntent(plan, request.OperationId, 1, admission, reservation)
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).StagingJournal.ShouldHaveSingleItem();
        failing.AppendStagingIntent(plan, request.OperationId, 1, admission, reservation)
            .IsOk.ShouldBeTrue();
        fixture.LoadOperation(request).StagingJournal.ShouldHaveSingleItem();
        Directory.Exists(plan.Envelope.Payload.Coordination.StagingRoot).ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task visible_creation_admission_after_directory_flush_failure_replays_one_token()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var admission = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 1, admission, reservation)
            .IsOk.ShouldBeTrue();
        var failing = fixture.CreateLedger(new FailNthDirectoryFlush(1));

        failing.AppendStagingCreationAdmission(
                plan, request.OperationId, 1, admission, reservation)
            .IsErr.ShouldBeTrue();
        var visible = fixture.LoadOperation(request);
        visible.StagingJournal[^1].Kind.ShouldBe(OperationStagingJournalKind.CreationAdmitted);
        var token = visible.StagingJournal[^1].CreationToken;
        token.ShouldNotBeNull();

        var replayed = failing.AppendStagingCreationAdmission(
                plan, request.OperationId, 1, admission, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        replayed.CreationToken.ShouldBe(token);
        fixture.LoadOperation(request).StagingJournal.ShouldBe(visible.StagingJournal);
        Directory.Exists(plan.Envelope.Payload.Coordination.StagingRoot).ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task tampered_staging_inventory_is_rejected_after_restart()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var admission = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 1, admission, reservation)
            .IsOk.ShouldBeTrue();
        var recordPath = DurableOperationLedger.GetRecordPath(
            fixture.LedgerRoot, request.RepositoryId, request.OperationId);
        var json = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        json["stagingJournal"]!.AsArray()[0]!["artifactPath"] = fixture.TargetPath;
        File.WriteAllText(recordPath, json.ToJsonString());

        fixture.CreatePlainLedger().Load(request.RepositoryId, request.OperationId)
            .Match(_ => throw new ShouldAssertException("Tampered inventory was accepted."),
                error => error.Kind)
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
    }

    [Theory]
    [InlineData("missing-journal")]
    [InlineData("changed-path")]
    [InlineData("changed-hash")]
    [InlineData("changed-token")]
    public async Task restart_rejects_missing_or_changed_no_effect_cleanup_inventory(
        string tampering)
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var admission = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 1, admission, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.AppendStagingCreationAdmission(
                plan, request.OperationId, 1, admission, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();

        var recordPath = DurableOperationLedger.GetRecordPath(
            fixture.LedgerRoot, request.RepositoryId, request.OperationId);
        var json = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        var cleanup = json["cleanupJournal"]!.AsArray();
        switch (tampering)
        {
            case "missing-journal":
                json.Remove("cleanupJournal");
                break;
            case "changed-path":
                cleanup[0]!["artifacts"]!.AsArray()[0]!["artifactPath"] = fixture.TargetPath;
                break;
            case "changed-hash":
                cleanup[0]!["artifacts"]!.AsArray()[0]!["expectedHash"] = new string('c', 64);
                break;
            case "changed-token":
                cleanup[0]!["artifacts"]!.AsArray()[0]!["creationToken"] = Guid.NewGuid().ToString();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(tampering));
        }
        File.WriteAllText(recordPath, json.ToJsonString());

        fixture.CreatePlainLedger().Load(request.RepositoryId, request.OperationId)
            .Match(_ => throw new ShouldAssertException("A broken cleanup inventory was accepted."),
                error => error.Kind)
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task creation_admission_without_its_token_is_rejected_after_restart()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var admission = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 1, admission, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.AppendStagingCreationAdmission(
                plan, request.OperationId, 1, admission, reservation)
            .IsOk.ShouldBeTrue();
        var recordPath = DurableOperationLedger.GetRecordPath(
            fixture.LedgerRoot, request.RepositoryId, request.OperationId);
        var json = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        json["stagingJournal"]!.AsArray()[1]!["creationToken"] = Guid.Empty.ToString();
        File.WriteAllText(recordPath, json.ToJsonString());

        fixture.CreatePlainLedger().Load(request.RepositoryId, request.OperationId)
            .Match(_ => throw new ShouldAssertException("A missing creation token was accepted."),
                error => error.Kind)
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
    }

    [Theory]
    [InlineData("mismatched-token")]
    [InlineData("missing-admission")]
    public async Task restart_rejects_a_mismatched_or_missing_staging_creation_admission(
        string tampering)
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var admission = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 1, admission, reservation)
            .IsOk.ShouldBeTrue();
        var creation = fixture.Ledger.AppendStagingCreationAdmission(
                plan, request.OperationId, 1, admission, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendStagingVerification(
                plan, request.OperationId, 1, Hash(fixture.Content), creation.CreationToken!.Value,
                admission, reservation)
            .IsOk.ShouldBeTrue();

        var recordPath = DurableOperationLedger.GetRecordPath(
            fixture.LedgerRoot, request.RepositoryId, request.OperationId);
        var json = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        var journal = json["stagingJournal"]!.AsArray();
        if (tampering == "mismatched-token")
        {
            journal[2]!["creationToken"] = Guid.NewGuid().ToString();
        }
        else
        {
            journal.RemoveAt(1);
            journal[1]!["sequence"] = 2;
        }
        File.WriteAllText(recordPath, json.ToJsonString());

        fixture.CreatePlainLedger().Load(request.RepositoryId, request.OperationId)
            .Match(_ => throw new ShouldAssertException("A broken staging phase was accepted."),
                error => error.Kind)
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
    }

    [Fact]
    public async Task staging_verification_rechecks_the_pinned_plan_bytes()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var admission = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 1, admission, reservation)
            .IsOk.ShouldBeTrue();
        var creation = fixture.Ledger.AppendStagingCreationAdmission(
                plan, request.OperationId, 1, admission, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var recordPath = DurableOperationLedger.GetRecordPath(
            fixture.LedgerRoot, request.RepositoryId, request.OperationId);
        var json = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        json["stagingJournal"]!.AsArray()[0]!["expectedHash"] = new string('c', 64);
        json["stagingJournal"]!.AsArray()[1]!["expectedHash"] = new string('c', 64);
        File.WriteAllText(recordPath, json.ToJsonString());

        fixture.Ledger.AppendStagingVerification(
                plan, request.OperationId, 1, new string('c', 64), creation.CreationToken!.Value,
                admission, reservation)
            .IsErr.ShouldBeTrue();
        fixture.CreatePlainLedger().Load(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task a_new_admission_cannot_take_over_a_prior_staging_intent()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var first = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 1, first, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, first, reservation)
            .IsOk.ShouldBeTrue();
        var second = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 1, second, reservation)
            .Match(_ => throw new ShouldAssertException("A second owner replaced the intent."),
                error => error.Kind)
            .ShouldBe(OperationLedgerErrorKind.Conflict);
        fixture.LoadOperation(request).StagingJournal.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task staged_replacements_follow_reviewed_effect_order()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(twoEffects: true);
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var admission = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 2, admission, reservation)
            .IsErr.ShouldBeTrue();
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 1, admission, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 2, admission, reservation)
            .IsErr.ShouldBeTrue();
        var creation = fixture.Ledger.AppendStagingCreationAdmission(
                plan, request.OperationId, 1, admission, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendStagingVerification(
                plan, request.OperationId, 1, Hash(fixture.Content), creation.CreationToken!.Value,
                admission, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 2, admission, reservation)
            .IsOk.ShouldBeTrue();
        fixture.LoadOperation(request).StagingJournal.Select(entry =>
            (entry.EffectSequence, entry.Kind)).ShouldBe([
            (1, OperationStagingJournalKind.Intent),
            (1, OperationStagingJournalKind.CreationAdmitted),
            (1, OperationStagingJournalKind.Verified),
            (2, OperationStagingJournalKind.Intent)
        ]);
    }

    [Fact]
    public async Task cancellation_closes_pre_effect_activity_while_an_admitted_write_drains()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        using var reservation = await fixture.Reservations.AcquireAsync(
            fixture.StoredPlan!.Envelope.Payload.Repository,
            CancellationToken.None);
        var admission = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendTransition(
                request.RepositoryId,
                request.OperationId,
                OperationLifecycleState.Prepared,
                OperationLifecycleState.CancelledNoEffect,
                "Unfenced cancellation")
            .IsErr.ShouldBeTrue();

        var cancelled = fixture.Ledger.CancelBeforeFirstEffect(
            request.RepositoryId,
            request.OperationId);

        cancelled.IsOk.ShouldBeTrue();
        var pending = fixture.LoadOperation(request);
        pending.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        pending.PreEffectActivity.IsClosed.ShouldBeTrue();
        pending.PreEffectActivity.ActiveAdmissions.ShouldContain(admission);
        pending.PreEffectActivity.ControlGeneration.ShouldBeGreaterThan(
            admission.ControlGeneration);
        fixture.Ledger.ConfirmDurableCancelledNoEffect(
                request.RepositoryId,
                request.OperationId)
            .IsErr.ShouldBeTrue();
        var otherRequest = fixture.Prepare();
        fixture.Ledger.HasUnresolvedRepositoryMutation(
                request.RepositoryId,
                otherRequest.OperationId)
            .Match(value => value, error => throw new IOException(error.Message))
            .ShouldBeTrue();
        fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                reservation).IsErr.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                admission with { Token = Guid.NewGuid() },
                reservation).IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).PreEffectActivity.ActiveAdmissions
            .ShouldContain(admission);
        File.Exists(fixture.TargetPath).ShouldBeFalse();

        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                admission,
                reservation).IsOk.ShouldBeTrue();
        var drained = fixture.LoadOperation(request);
        drained.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        drained.PreEffectActivity.IsClosed.ShouldBeTrue();
        drained.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        fixture.Ledger.ConfirmDurableCancelledNoEffect(
                request.RepositoryId,
                request.OperationId)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                admission,
                reservation)
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        fixture.Ledger.HasUnresolvedRepositoryMutation(
                request.RepositoryId,
                otherRequest.OperationId)
            .Match(value => value, error => throw new IOException(error.Message))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task a_new_reservation_cannot_complete_an_earlier_admission()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var first = await fixture.Reservations.AcquireAsync(
            fixture.StoredPlan!.Envelope.Payload.Repository,
            CancellationToken.None);
        var admission = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                first)
            .Match(value => value, error => throw new IOException(error.Message));
        first.Dispose();
        using var later = await fixture.Reservations.AcquireAsync(
            fixture.StoredPlan.Envelope.Payload.Repository,
            CancellationToken.None);

        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                admission,
                later)
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).PreEffectActivity.ActiveAdmissions
            .ShouldContain(admission);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task first_effect_intent_waits_for_pre_effect_activity_and_defeats_late_cancellation()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository,
            CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.ClaimApplyOwner(
                request.RepositoryId,
                request.OperationId,
                reservation,
                request.Identity.PlanHash)
            .IsOk.ShouldBeTrue();
        var effect = plan.Envelope.Payload.Effects[0];
        var effectHash = CanonicalChangePlan.ComputeEffectHash(effect);
        var intentHash = OperationEffectIntentIdentity.Compute(
            request.Identity.PlanHash,
            effect.Sequence,
            reservation.FenceToken,
            reservation.FenceEpoch);

        fixture.Ledger.AppendEffectIntent(
                request.RepositoryId,
                request.OperationId,
                effect.Sequence,
                intentHash,
                reservation,
                effectHash,
                OperationGuardTokenSet.Expected(plan.Envelope.Payload))
            .IsErr.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                activity,
                reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.AppendEffectIntent(
                request.RepositoryId,
                request.OperationId,
                effect.Sequence,
                intentHash,
                reservation,
                effectHash,
                OperationGuardTokenSet.Expected(plan.Envelope.Payload))
            .IsOk.ShouldBeTrue();

        fixture.Ledger.CancelBeforeFirstEffect(
                request.RepositoryId,
                request.OperationId)
            .IsErr.ShouldBeTrue();
        var reloaded = fixture.LoadOperation(request);
        reloaded.CurrentState.ShouldBe(OperationLifecycleState.Applying);
        reloaded.PreEffectActivity.IsClosed.ShouldBeTrue();
        reloaded.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        reloaded.EffectJournal.Single().Kind.ShouldBe(OperationEffectJournalKind.Intent);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task cancellation_during_final_preflight_drains_control_activity_without_an_effect()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture) { CancelDuringPreflight = true };

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsOk.ShouldBeTrue();
        var reloaded = fixture.LoadOperation(request);
        reloaded.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        reloaded.PreEffectActivity.IsClosed.ShouldBeTrue();
        reloaded.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        reloaded.EffectJournal.ShouldBeEmpty();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public void uncertain_cancellation_durability_blocks_later_mutation()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var failingLedger = fixture.CreateLedger(new FailDirectoryFlushFrom(1));

        failingLedger.CancelBeforeFirstEffect(
                request.RepositoryId,
                request.OperationId)
            .IsErr.ShouldBeTrue();
        var visible = fixture.LoadOperation(request);
        visible.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        visible.EffectJournal.ShouldBeEmpty();
        var otherRequest = fixture.Prepare();

        failingLedger.HasUnresolvedRepositoryMutation(
                request.RepositoryId,
                otherRequest.OperationId)
            .IsErr.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public void a_visible_cancellation_requires_durable_confirmation_before_success()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var failingLedger = fixture.CreateLedger(new FailDirectoryFlushFrom(1));
        failingLedger.CancelBeforeFirstEffect(
                request.RepositoryId,
                request.OperationId)
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).CurrentState
            .ShouldBe(OperationLifecycleState.CancelledNoEffect);

        failingLedger.ConfirmDurableCancelledNoEffect(
                request.RepositoryId,
                request.OperationId)
            .IsErr.ShouldBeTrue();
        fixture.Ledger.ConfirmDurableCancelledNoEffect(
                request.RepositoryId,
                request.OperationId)
            .IsOk.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task interrupted_cancellation_retains_its_cleanup_inventory_until_durable_confirmation()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 1, activity, reservation)
            .IsOk.ShouldBeTrue();
        var creation = fixture.Ledger.AppendStagingCreationAdmission(
                plan, request.OperationId, 1, activity, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var failing = fixture.CreateLedger(new FailNthDirectoryFlush(1));

        failing.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsErr.ShouldBeTrue();

        var visible = fixture.LoadOperation(request);
        visible.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        visible.CleanupJournal.ShouldHaveSingleItem();
        visible.CleanupJournal[0].Artifacts.ShouldHaveSingleItem();
        visible.CleanupJournal[0].Artifacts[0].CreationToken.ShouldBe(creation.CreationToken!.Value);
        fixture.Ledger.AppendStagingVerification(
                plan, request.OperationId, 1, Hash(fixture.Content), creation.CreationToken.Value,
                activity, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();

        failing.ConfirmDurableCancelledNoEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();
        fixture.LoadOperation(request).CleanupJournal[0].Artifacts[0].ArtifactPath
            .ShouldBe(creation.ArtifactPath);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public async Task pre_effect_fence_persistence_failure_prevents_repository_effect(
        int failedFlush,
        bool brokerWasAcquired)
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture);

        var result = await fixture.CreateAuthority(
            broker,
            new FailNthDirectoryFlush(failedFlush)).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.LedgerUnavailable);
        broker.Acquired.ShouldBe(brokerWasAcquired);
        fixture.LoadOperation(request).CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        fixture.LoadOperation(request).EffectJournal.ShouldBeEmpty();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task uncertain_activity_completion_durability_blocks_later_mutation()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var failingDurability = new FailDirectoryFlushFrom(2);

        (await fixture.CreateAuthority(
                new SimulatedBroker(fixture), failingDurability).ApplyAsync(request))
            .IsErr.ShouldBeTrue();
        var visible = fixture.LoadOperation(request);
        visible.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        visible.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        var otherRequest = fixture.Prepare();

        fixture.CreateLedger(failingDurability).HasUnresolvedRepositoryMutation(
                request.RepositoryId,
                request.OperationId)
            .IsErr.ShouldBeTrue();
        fixture.CreateLedger(failingDurability).HasUnresolvedRepositoryMutation(
                request.RepositoryId,
                otherRequest.OperationId)
            .IsErr.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task a_restarted_apply_does_not_acquire_guards_while_prior_control_activity_is_unresolved()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        (await fixture.CreateAuthority(
                new SimulatedBroker(fixture),
                new FailNthDirectoryFlush(1)).ApplyAsync(request))
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).PreEffectActivity.ActiveAdmissions
            .ShouldHaveSingleItem();
        var replayBroker = new SimulatedBroker(fixture);

        var replay = await fixture.CreateAuthority(replayBroker).ApplyAsync(request);

        replay.IsErr.ShouldBeTrue();
        replay.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.RecoveryRequired);
        replayBroker.Acquired.ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task no_effect_broker_fails_closed_before_applying()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();

        var result = await fixture.CreateAuthority().ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.UnsafeCoordination);
        var restarted = fixture.LoadOperation(request);
        restarted.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        restarted.EffectJournal.ShouldBeEmpty();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task guarded_effect_is_journaled_and_verified_before_applied()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture);

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsOk.ShouldBeTrue();
        var restarted = fixture.LoadOperation(request);
        restarted.CurrentState.ShouldBe(OperationLifecycleState.Applied);
        restarted.EffectJournal.Select(entry => entry.Kind).ShouldBe([
            OperationEffectJournalKind.Intent,
            OperationEffectJournalKind.Admitted,
            OperationEffectJournalKind.Observed
        ]);
        restarted.EffectJournal[0].FenceEpoch.ShouldNotBeNull();
        restarted.EffectJournal[0].FenceEpoch!.Value.ShouldBeGreaterThan(0);
        restarted.EffectJournal[0].FenceToken.ShouldNotBeNull();
        restarted.EffectJournal[0].EffectHash.ShouldBe(
            CanonicalChangePlan.ComputeEffectHash(
                fixture.StoredPlan!.Envelope.Payload.Effects[0]));
        restarted.EffectJournal[1].EffectHash.ShouldBe(restarted.EffectJournal[0].EffectHash);
        restarted.EffectJournal[2].EffectHash.ShouldBe(restarted.EffectJournal[0].EffectHash);
        restarted.EffectJournal[0].GuardTokens.ShouldBe(
            OperationGuardTokenSet.Expected(fixture.StoredPlan.Envelope.Payload));
        restarted.EffectJournal[1].GuardTokens.ShouldBe(restarted.EffectJournal[0].GuardTokens);
        restarted.EffectJournal[2].GuardTokens.ShouldBe(restarted.EffectJournal[0].GuardTokens);
        restarted.OwnerLease.ShouldNotBeNull();
        restarted.OwnerLease!.FenceEpoch.ShouldBe(restarted.EffectJournal[0].FenceEpoch!.Value);
        restarted.OwnerLease.ReservationToken.ShouldBe(restarted.EffectJournal[0].FenceToken!.Value);
        broker.SawDurableIntent.ShouldBeTrue();
        broker.SawDurableAdmission.ShouldBeTrue();
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.Content);
        broker.ReservationWasHeldThroughVerification.ShouldBeTrue();
    }

    [Theory]
    [InlineData(SimulatedFault.BeforeEffect, false)]
    [InlineData(SimulatedFault.AfterAdmission, false)]
    [InlineData(SimulatedFault.AfterEffect, true)]
    [InlineData(SimulatedFault.BeforeFinalVerification, true)]
    public async Task interrupted_effects_retain_truthful_recovery_state(
        SimulatedFault fault,
        bool targetWasWritten)
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture) { Fault = fault };

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.RecoveryRequired);
        var restarted = fixture.LoadOperation(request);
        restarted.CurrentState.ShouldBe(OperationLifecycleState.RecoveryRequired);
        restarted.CurrentState.ShouldNotBe(OperationLifecycleState.Applied);
        restarted.CurrentState.ShouldNotBe(OperationLifecycleState.RolledBack);
        restarted.EffectJournal[0].Kind.ShouldBe(OperationEffectJournalKind.Intent);
        File.Exists(fixture.TargetPath).ShouldBe(targetWasWritten);
        broker.SawDurableIntent.ShouldBeTrue();
    }

    [Fact]
    public async Task broker_cannot_start_when_plan_is_not_pinned_to_the_operation()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(pin: false);
        var broker = new SimulatedBroker(fixture);

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.PlanUnavailable);
        broker.Acquired.ShouldBeFalse();
        fixture.LoadOperation(request).CurrentState.ShouldBe(OperationLifecycleState.Prepared);
    }

    [Fact]
    public async Task incomplete_guard_evidence_cannot_enter_applying()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture) { OmitOneGuard = true };

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.UnsafeCoordination);
        fixture.LoadOperation(request).CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        fixture.LoadOperation(request).EffectJournal.ShouldBeEmpty();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task a_broker_that_skips_one_use_admission_cannot_claim_applied()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture) { SkipAdmission = true };

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.RecoveryRequired);
        var restarted = fixture.LoadOperation(request);
        restarted.CurrentState.ShouldBe(OperationLifecycleState.RecoveryRequired);
        restarted.EffectJournal.ShouldHaveSingleItem();
        restarted.EffectJournal[0].Kind.ShouldBe(OperationEffectJournalKind.Intent);
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.Content);
    }

    [Fact]
    public async Task expired_owner_cannot_admit_a_repository_effect()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture) { ExpireBeforeAdmission = true };

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.RecoveryRequired);
        var restarted = fixture.LoadOperation(request);
        restarted.CurrentState.ShouldBe(OperationLifecycleState.RecoveryRequired);
        restarted.EffectJournal.ShouldHaveSingleItem();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task expired_owner_after_effect_cannot_publish_applied()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture) { ExpireBeforeFinalVerification = true };

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.RecoveryRequired);
        var restarted = fixture.LoadOperation(request);
        restarted.CurrentState.ShouldBe(OperationLifecycleState.RecoveryRequired);
        restarted.EffectJournal[^1].Kind.ShouldBe(OperationEffectJournalKind.Observed);
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.Content);
    }

    [Fact]
    public async Task an_uncertain_observation_publication_stays_recovery_required()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture);

        var result = await fixture.CreateAuthority(
            broker,
            new FailNthDirectoryFlush(7)).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).CurrentState
            .ShouldBe(OperationLifecycleState.RecoveryRequired);
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.Content);
    }

    [Fact]
    public async Task an_admission_flush_failure_cannot_start_the_effect()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture);

        var result = await fixture.CreateAuthority(
            broker,
            new FailNthDirectoryFlush(6)).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        var restarted = fixture.LoadOperation(request);
        restarted.CurrentState.ShouldBe(OperationLifecycleState.RecoveryRequired);
        restarted.EffectJournal[^1].Kind.ShouldBe(OperationEffectJournalKind.Admitted);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task an_admission_replace_failure_cannot_start_the_effect()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture);

        var result = await fixture.CreateAuthority(
            broker,
            new FailNthReplace(6)).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        var restarted = fixture.LoadOperation(request);
        restarted.CurrentState.ShouldBe(OperationLifecycleState.RecoveryRequired);
        restarted.EffectJournal[^1].Kind.ShouldBe(OperationEffectJournalKind.Intent);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task an_uncertain_terminal_flush_requires_a_successful_flush_retry()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture);

        var result = await fixture.CreateAuthority(
            broker,
            new FailNthDirectoryFlush(9)).ApplyAsync(request);

        result.IsOk.ShouldBeTrue();
        fixture.LoadOperation(request).CurrentState.ShouldBe(OperationLifecycleState.Applied);
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.Content);
    }

    [Fact]
    public async Task persistent_terminal_flush_failure_blocks_later_mutation()
    {
        using var fixture = new ApplyFixture();
        var firstRequest = fixture.Prepare();
        var durability = new FailDirectoryFlushFrom(9);

        var first = await fixture.CreateAuthority(
            new SimulatedBroker(fixture), durability).ApplyAsync(firstRequest);

        first.IsErr.ShouldBeTrue();
        first.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.LedgerUnavailable);
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.Content);

        var secondRequest = fixture.Prepare();
        var secondBroker = new SimulatedBroker(fixture);
        var second = await fixture.CreateAuthority(
            secondBroker, durability).ApplyAsync(secondRequest);

        second.IsErr.ShouldBeTrue();
        second.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.LedgerUnavailable);
        secondBroker.Acquired.ShouldBeFalse();
        fixture.LoadOperation(secondRequest).CurrentState.ShouldBe(OperationLifecycleState.Prepared);
    }

    [Fact]
    public async Task unresolved_mutation_blocks_the_next_operation_before_broker_admission()
    {
        using var fixture = new ApplyFixture();
        var firstRequest = fixture.Prepare();
        var interrupted = new SimulatedBroker(fixture) { Fault = SimulatedFault.AfterEffect };
        (await fixture.CreateAuthority(interrupted).ApplyAsync(firstRequest))
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(firstRequest).CurrentState
            .ShouldBe(OperationLifecycleState.RecoveryRequired);

        var secondRequest = fixture.Prepare();
        var nextBroker = new SimulatedBroker(fixture);
        var second = await fixture.CreateAuthority(nextBroker).ApplyAsync(secondRequest);

        second.IsErr.ShouldBeTrue();
        second.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.RecoveryRequired);
        nextBroker.Acquired.ShouldBeFalse();
        fixture.LoadOperation(secondRequest).CurrentState.ShouldBe(OperationLifecycleState.Prepared);
    }

    public enum SimulatedFault
    {
        None,
        BeforeEffect,
        AfterAdmission,
        AfterEffect,
        BeforeFinalVerification
    }

    private sealed class SimulatedBroker(ApplyFixture fixture) : IJournaledRepositoryEffectBroker
    {
        internal SimulatedFault Fault { get; init; }
        internal bool OmitOneGuard { get; init; }
        internal bool SkipAdmission { get; init; }
        internal bool ExpireBeforeAdmission { get; init; }
        internal bool ExpireBeforeFinalVerification { get; init; }
        internal bool CancelDuringPreflight { get; init; }
        internal bool Acquired { get; private set; }
        internal bool SawDurableIntent { get; private set; }
        internal bool SawDurableAdmission { get; private set; }
        internal bool ReservationWasHeldThroughVerification { get; private set; }

        public Task<IJournaledRepositoryEffectSession?> AcquireAsync(
            StoredChangePlan plan,
            RepositoryMutationReservation reservation,
            CancellationToken cancellationToken)
        {
            reservation.AssertHeld(plan.Key.RepositoryId);
            Acquired = true;
            return Task.FromResult<IJournaledRepositoryEffectSession?>(
                new Session(this, fixture, plan, reservation));
        }

        private sealed class Session(
            SimulatedBroker broker,
            ApplyFixture fixture,
            StoredChangePlan plan,
            RepositoryMutationReservation reservation) : IJournaledRepositoryEffectSession
        {
            public Task<GuardedPreflightEvidence?> FinalPreflightAsync(
                CancellationToken cancellationToken)
            {
                reservation.AssertHeld("repository-001");
                if (broker.CancelDuringPreflight)
                {
                    fixture.Ledger.CancelBeforeFirstEffect(
                            fixture.Request!.RepositoryId,
                            fixture.Request.OperationId)
                        .IsOk.ShouldBeTrue();
                }
                return Task.FromResult<GuardedPreflightEvidence?>(
                    File.Exists(fixture.TargetPath)
                        ? null
                        : new GuardedPreflightEvidence(
                            broker.OmitOneGuard
                                ? OperationGuardTokenSet.Expected(plan.Envelope.Payload)
                                    .RemoveAt(OperationGuardTokenSet.Expected(plan.Envelope.Payload).Length - 1)
                                : OperationGuardTokenSet.Expected(plan.Envelope.Payload)));
            }

            public Task<string> ApplyEffectAsync(
                ChangePlanEffect effect,
                RepositoryEffectAdmission admission,
                CancellationToken cancellationToken)
            {
                reservation.AssertHeld("repository-001");
                var operation = fixture.LoadOperation(fixture.Request!);
                broker.SawDurableIntent = operation.EffectJournal.Length == 1
                    && operation.EffectJournal[0].Kind == OperationEffectJournalKind.Intent
                    && operation.EffectJournal[0].EvidenceHash == admission.IntentHash;
                if (!broker.SawDurableIntent)
                    throw new IOException("No durable intent preceded the simulated effect.");
                if (broker.Fault == SimulatedFault.BeforeEffect)
                    throw new IOException("Injected interruption before effect.");
                if (broker.ExpireBeforeAdmission)
                    fixture.Now += TimeSpan.FromMinutes(2);
                if (!broker.SkipAdmission)
                {
                    admission.Admit();
                    broker.SawDurableAdmission = fixture.LoadOperation(fixture.Request!)
                        .EffectJournal[^1].Kind == OperationEffectJournalKind.Admitted;
                }
                if (broker.Fault == SimulatedFault.AfterAdmission)
                    throw new IOException("Injected interruption after admission.");
                File.WriteAllBytes(fixture.TargetPath, effect.ReplacementContent.ToArray());
                if (broker.Fault == SimulatedFault.AfterEffect)
                    throw new IOException("Injected interruption after effect.");
                return Task.FromResult(Hash(File.ReadAllBytes(fixture.TargetPath)));
            }

            public Task<string?> VerifyAppliedAsync(CancellationToken cancellationToken)
            {
                reservation.AssertHeld("repository-001");
                broker.ReservationWasHeldThroughVerification = true;
                if (broker.ExpireBeforeFinalVerification)
                    fixture.Now += TimeSpan.FromMinutes(2);
                if (broker.Fault == SimulatedFault.BeforeFinalVerification)
                    throw new IOException("Injected interruption before final verification.");
                return Task.FromResult<string?>(
                    File.ReadAllBytes(fixture.TargetPath).SequenceEqual(fixture.Content)
                        ? Hash(fixture.Content)
                        : null);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingStagingDurability(int? failedFlush = null)
        : IStagingArtifactDurability
    {
        internal List<string> Flushes { get; } = [];

        public void FlushFile(FileStream stream)
        {
            Record("file");
            stream.Flush(flushToDisk: true);
        }

        public void FlushDirectory(SafeFileHandle handle)
        {
            Record("directory");
            LinuxStagingHandles.FlushDirectory(handle);
        }

        private void Record(string kind)
        {
            Flushes.Add(kind);
            if (Flushes.Count == failedFlush)
                throw new IOException($"Injected failure at staging {kind} flush {failedFlush}.");
        }
    }

    private sealed class ThrowAfterArtifactBoundary(
        IStagingArtifactBoundary inner,
        ApplyFixture fixture,
        CanonicalApplyRequest request)
        : IStagingArtifactBoundary
    {
        public IStagingArtifactSession Acquire(StoredChangePlan plan) =>
            new Session(inner.Acquire(plan), fixture, request);

        private sealed class Session(
            IStagingArtifactSession inner,
            ApplyFixture fixture,
            CanonicalApplyRequest request) : IStagingArtifactSession
        {
            public async Task<string> CreateAndHashAsync(
                OperationStagingJournalEvent creation,
                ReadOnlyMemory<byte> replacementBytes,
                bool allowExistingAdmittedArtifact)
            {
                var journal = fixture.LoadOperation(request).StagingJournal;
                journal.Select(entry => entry.Kind).ShouldBe([
                    OperationStagingJournalKind.Intent,
                    OperationStagingJournalKind.CreationAdmitted
                ]);
                journal[^1].CreationToken.ShouldBe(creation.CreationToken);
                journal[^1].ArtifactPath.ShouldBe(creation.ArtifactPath);
                File.Exists(creation.ArtifactPath).ShouldBeFalse();
                await inner.CreateAndHashAsync(
                    creation, replacementBytes, allowExistingAdmittedArtifact);
                throw new IOException("Injected interruption after durable artifact creation.");
            }

            public Task<string> VerifyExistingAsync(
                OperationStagingJournalEvent verification,
                ReadOnlyMemory<byte> replacementBytes) =>
                inner.VerifyExistingAsync(verification, replacementBytes);

            public void Dispose() => inner.Dispose();
        }
    }

    private sealed class TouchTrackingArtifactBoundary(IStagingArtifactBoundary inner)
        : IStagingArtifactBoundary
    {
        internal bool Touched { get; private set; }

        public IStagingArtifactSession Acquire(StoredChangePlan plan) =>
            new Session(inner.Acquire(plan), this);

        private sealed class Session(
            IStagingArtifactSession inner,
            TouchTrackingArtifactBoundary boundary) : IStagingArtifactSession
        {
            public Task<string> CreateAndHashAsync(
                OperationStagingJournalEvent creation,
                ReadOnlyMemory<byte> replacementBytes,
                bool allowExistingAdmittedArtifact)
            {
                boundary.Touched = true;
                return inner.CreateAndHashAsync(
                    creation, replacementBytes, allowExistingAdmittedArtifact);
            }

            public Task<string> VerifyExistingAsync(
                OperationStagingJournalEvent verification,
                ReadOnlyMemory<byte> replacementBytes)
            {
                boundary.Touched = true;
                return inner.VerifyExistingAsync(verification, replacementBytes);
            }

            public void Dispose() => inner.Dispose();
        }
    }

    private sealed class ApplyFixture : IDisposable
    {
        private static readonly DateTimeOffset InitialNow =
            new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "rt-journaled-apply-" + Guid.NewGuid().ToString("N"));
        private readonly LocalDurableOperationAuthority _authority;
        private readonly string? _separateControlRoot;
        private readonly DurableChangePlanStore _store;
        private readonly DurableOperationLedger _ledger;

        internal ApplyFixture(bool differentMountControlRoot = false)
        {
            RepositoryRoot = Path.Combine(_root, "repository");
            Directory.CreateDirectory(Path.Combine(RepositoryRoot, "src"));
            _separateControlRoot = differentMountControlRoot
                ? Path.Combine("/dev/shm", "rt-journaled-control-" + Guid.NewGuid().ToString("N"))
                : null;
            _authority = new LocalDurableOperationAuthority(
                _separateControlRoot ?? Path.Combine(_root, "control"), () => Now);
            _store = new DurableChangePlanStore(
                _authority.PlanStoreRoot,
                ChangePlanStoreOptions.Default,
                () => Now,
                _authority);
            _ledger = new DurableOperationLedger(_authority.LedgerRoot, () => Now, _authority);
        }

        internal string RepositoryRoot { get; }
        internal DateTimeOffset Now { get; set; } = InitialNow;
        internal string TargetPath => Path.Combine(RepositoryRoot, "src", "Added.cs");
        internal byte[] Content { get; } = Encoding.UTF8.GetBytes("class Added;\n");
        internal CanonicalApplyRequest? Request { get; private set; }
        internal StoredChangePlan? StoredPlan { get; private set; }
        internal DurableOperationLedger Ledger => _ledger;
        internal DurableChangePlanStore Store => _store;
        internal string LedgerRoot => _authority.LedgerRoot;
        internal DurableOperationLedger CreatePlainLedger() =>
            new(_authority.LedgerRoot, () => Now, _authority);
        internal StoredChangePlan ReloadPlan() =>
            _store.Load(StoredPlan!.Key)
                .Match(value => value, error => throw new IOException(error.Message));
        internal RepositoryMutationReservationProvider Reservations => new(
            _authority.MutationReservationRoot, _authority);
        internal DurableOperationLedger CreateLedger(IOperationLedgerDurability durability) =>
            new(_authority.LedgerRoot, () => Now, _authority, durability);

        internal CanonicalApplyRequest Prepare(bool pin = true, bool twoEffects = false)
        {
            var envelope = CreateEnvelope(twoEffects);
            var key = _store.Persist(envelope).Match(value => value, error => throw new IOException(error.Message));
            var operationId = new OperationId(42, Guid.NewGuid());
            var claims = new OperationEpochClaims(
                "repository-001", "principal-001", 42, 8, 1024 * 1024, Now.AddHours(1));
            var request = CanonicalApplyRequest.Create(
                key.RepositoryId,
                operationId,
                key.PlanId,
                key.PlanHash,
                envelope.Payload.Repository.BaseRevision,
                claims);
            Request = request;
            if (pin)
            {
                _store.PinDispatchLease(key, "dispatch-001", Now.AddMinutes(10))
                    .Match(value => value, error => throw new IOException(error.Message));
                _store.AdoptDispatchLease(
                    key,
                    "dispatch-001",
                    DurableDispatchIntakeCoordinator.FormatOperationReference(operationId))
                    .Match(value => value, error => throw new IOException(error.Message));
            }
            StoredPlan = _store.Load(key).Match(value => value, error => throw new IOException(error.Message));
            _ledger.Receive(request, _authority.GetPrincipal())
                .Match(value => value, error => throw new IOException(error.Message));
            _ledger.AppendTransition(
                    request.RepositoryId,
                    request.OperationId,
                    OperationLifecycleState.Received,
                    OperationLifecycleState.Prepared,
                    "Exact plan pinned.")
                .Match(value => value, error => throw new IOException(error.Message));
            return request;
        }

        internal JournaledApplyAuthority CreateAuthority(
            IJournaledRepositoryEffectBroker? broker = null,
            IOperationLedgerDurability? ledgerDurability = null) => new(
            _store,
            ledgerDurability is null
                ? _ledger
                : new DurableOperationLedger(
                    _authority.LedgerRoot, () => Now, _authority, ledgerDurability),
            new RepositoryMutationReservationProvider(_authority.MutationReservationRoot, _authority),
            broker);

        internal DurableOperationRecord LoadOperation(CanonicalApplyRequest request) =>
            new DurableOperationLedger(_authority.LedgerRoot, () => Now, _authority)
                .Load(request.RepositoryId, request.OperationId)
                .Match(value => value, error => throw new IOException(error.Message));

        private ChangePlanEnvelope CreateEnvelope(bool twoEffects)
        {
            var controlRoot = _authority.ControlRoot;
            var sourcePath = "src/Added.cs";
            var after = new ChangePlanPathState(
                true, ChangePlanPathKind.RegularFile, Hash(Content), null, 1, [],
                "utf-8", false, "lf", "0644", false, false, null);
            var directory = new ChangePlanPathState(
                true, ChangePlanPathKind.Directory, null, "device:test;inode:src", 2, [],
                null, null, null, "0755", true, false, null);
            var operations = new List<ChangePlanOperation>
            {
                new(1, "create", sourcePath, [])
            };
            var effects = new List<ChangePlanEffect>
            {
                new(1, ChangePlanEffectKind.Create, ChangePlanArtifactKind.RoslynDocument,
                    sourcePath, TargetPath, null, null,
                    ChangePlanPathState.Missing, after, null, null, null, Content,
                    [new ChangePlanGuardRequirement(
                        sourcePath, ChangePlanGuardKind.ExternalWriteGuard, "guard-target")])
            };
            if (twoEffects)
            {
                const string secondPath = "src/Added2.cs";
                operations.Add(new ChangePlanOperation(2, "create", secondPath, []));
                effects.Add(new ChangePlanEffect(
                    2, ChangePlanEffectKind.Create, ChangePlanArtifactKind.RoslynDocument,
                    secondPath, Path.Combine(RepositoryRoot, "src", "Added2.cs"), null, null,
                    ChangePlanPathState.Missing, after, null, null, null, Content,
                    [new ChangePlanGuardRequirement(
                        secondPath, ChangePlanGuardKind.ExternalWriteGuard, "guard-target-2")]));
            }
            var payload = new ChangePlanPayload(
                Guid.NewGuid(),
                new ChangePlanRepository(
                    "repository-001", RepositoryRoot, RepositoryRoot,
                    "device:test;inode:repository", new string('a', 40), new string('b', 64)),
                Now.AddMinutes(-1), Now.AddMinutes(20),
                operations,
                [new ChangePlanDependency(
                    "src", Path.Combine(RepositoryRoot, "src"),
                    ChangePlanDependencyKind.DirectoryEntry, directory,
                    new ChangePlanGuardRequirement("src", ChangePlanGuardKind.ExternalWriteGuard, "guard-dir"))],
                effects,
                [], [], new ChangePlanConfidence("Proof", "Complete workspace", ["All inputs loaded"]), [],
                new ChangePlanWorkspace(
                    WorkspaceCompletenessState.Complete,
                    Path.Combine(RepositoryRoot, "RoslynTools.slnx"),
                    "Debug", ["net10.0"], ".NET SDK 10.0.100",
                    "Microsoft.CodeAnalysis 5.0.0", "InProcess", "Trusted"),
                [new ChangePlanVerificationAction(1, "build", "dotnet build", "success", true)],
                new ChangePlanCoordination(
                    ChangePlanCoordinationDomain.ExclusivePrincipal,
                    ChangePlanCoordinationMode.ExternalWriteGuards,
                    "one private writer", controlRoot, controlRoot,
                    "device:test;inode:control", "device:test",
                    Path.Combine(controlRoot, "staging"), Path.Combine(controlRoot, "staging"),
                    "device:test;inode:staging",
                    Path.Combine(controlRoot, "backup"), Path.Combine(controlRoot, "backup"),
                    "device:test;inode:backup"),
                new ChangePlanPathPolicy(
                    ChangePlanCaseSensitivity.Sensitive, ChangePlanLongPathPolicy.Reject,
                    ChangePlanReadOnlyPolicy.Reject, ChangePlanSymlinkPolicy.Reject,
                    ChangePlanHardLinkPolicy.Reject),
                ChangePlanCleanupPolicy.RetainUntilVerifiedTerminal,
                ChangePlanRecoveryPolicy.RequireDurableJournal);
            return ChangePlanEnvelope.Create(payload);
        }

        public void Dispose()
        {
            if (_separateControlRoot is not null && Directory.Exists(_separateControlRoot))
                Directory.Delete(_separateControlRoot, recursive: true);
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }

    private static string Hash(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA2101", Justification =
        "Linux libc paths use UTF-8 and both arguments are explicitly marshaled as LPUTF8Str.")]
    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string existingPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA2101", Justification =
        "Linux libc paths use UTF-8 and the argument is explicitly marshaled as LPUTF8Str.")]
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);

    private sealed class FailNthDirectoryFlush(int failureNumber) : IOperationLedgerDurability
    {
        private int _directoryFlushCount;

        public void FlushFileMetadata(string path)
        {
        }

        public void ReplaceFile(string temporaryPath, string recordPath) =>
            File.Move(temporaryPath, recordPath, overwrite: true);

        public void FlushDirectoryMetadata(string directory)
        {
            if (Interlocked.Increment(ref _directoryFlushCount) == failureNumber)
                throw new IOException("Injected directory flush failure.");
        }
    }

    private sealed class FailDirectoryFlushFrom(int failureNumber) : IOperationLedgerDurability
    {
        private int _directoryFlushCount;

        public void FlushFileMetadata(string path)
        {
        }

        public void ReplaceFile(string temporaryPath, string recordPath) =>
            File.Move(temporaryPath, recordPath, overwrite: true);

        public void FlushDirectoryMetadata(string directory)
        {
            if (Interlocked.Increment(ref _directoryFlushCount) >= failureNumber)
                throw new IOException("Injected persistent directory flush failure.");
        }
    }

    private sealed class FailNthReplace(int failureNumber) : IOperationLedgerDurability
    {
        private int _replaceCount;

        public void FlushFileMetadata(string path)
        {
        }

        public void ReplaceFile(string temporaryPath, string recordPath)
        {
            if (Interlocked.Increment(ref _replaceCount) == failureNumber)
                throw new IOException("Injected record replacement failure.");
            File.Move(temporaryPath, recordPath, overwrite: true);
        }

        public void FlushDirectoryMetadata(string directory)
        {
        }
    }
}
