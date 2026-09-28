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
    public async Task disposed_reservation_releases_local_lock_after_all_control_activities_leave()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var first = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var one = first.HoldControlActivity(request.RepositoryId);
        var two = first.HoldControlActivity(request.RepositoryId);
        first.Dispose();
        Should.Throw<ObjectDisposedException>(() => first.HoldControlActivity(request.RepositoryId));

        var successorTask = fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        try
        {
            await Task.Delay(100);
            successorTask.IsCompleted.ShouldBeFalse();
            one.Dispose();
            await Task.Delay(100);
            successorTask.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            one.Dispose();
            two.Dispose();
        }
        using var successor = await successorTask;
        successor.FenceEpoch.ShouldBeGreaterThan(first.FenceEpoch);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task successor_waits_for_in_flight_staging_after_owner_releases_reservation()
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
        var first = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(request.RepositoryId, request.OperationId, first)
            .Match(value => value, error => throw new IOException(error.Message));
        var pausedBoundary = new PausingStagingBoundary(
            new LinuxStagingArtifactBoundary(stagingRoot));
        var staging = new JournaledStagingArtifactWriter(fixture.Ledger, pausedBoundary)
            .StageReplacementAsync(plan, request.OperationId, 1, activity, first);
        await pausedBoundary.Entered.WaitAsync(TimeSpan.FromSeconds(5));
        first.Dispose();

        var successorTask = fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        try
        {
            await Task.Delay(100);
            successorTask.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            pausedBoundary.Resume();
        }
        await Should.ThrowAsync<ObjectDisposedException>(async () => await staging);
        using var successor = await successorTask;
        fixture.LoadOperation(request).PreEffectActivity.ActiveAdmissions
            .ShouldContain(activity);
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, successor)
            .IsErr.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task successor_reconciles_interrupted_activity_without_artifacts_and_releases_pin()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var first = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, first)
            .Match(value => value, error => throw new IOException(error.Message));
        first.Dispose();
        var restarted = fixture.RestartedComponents();
        using var successor = await restarted.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);

        var result = new JournaledNoEffectCleanup(
                restarted.Ledger, restarted.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
                new LinuxBackupCleanupBoundary(plan.Envelope.Payload.Coordination.BackupRoot))
            .Cleanup(request.RepositoryId, request.OperationId, successor);

        var reconciled = result.Match(value => value, error => throw new IOException(error.Message));
        reconciled.CurrentState.ShouldBe(OperationLifecycleState.RejectedUnsafeCoordination);
        reconciled.PreEffectActivity.IsClosed.ShouldBeTrue();
        reconciled.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        reconciled.CleanupJournal.Select(entry => entry.Kind).ShouldBe([
            OperationCleanupJournalKind.InventoryPending,
            OperationCleanupJournalKind.CleanupVerified
        ]);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
        restarted.Ledger.Load(request.RepositoryId, request.OperationId).IsOk.ShouldBeTrue();
        restarted.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, successor)
            .IsErr.ShouldBeTrue();
        restarted.Ledger.AppendStagingIntent(
                plan, request.OperationId, 1, activity, successor)
            .IsErr.ShouldBeTrue();
    }

    [Fact]
    public async Task successor_reconciles_interrupted_staging_and_cleans_admitted_artifact()
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
        var first = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(request.RepositoryId, request.OperationId, first)
            .Match(value => value, error => throw new IOException(error.Message));
        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, first);
        var artifact = staged.Match(value => value, error => throw new IOException(error.Message));
        first.Dispose();
        var restarted = fixture.RestartedComponents();
        using var successor = await restarted.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var backupRoot = plan.Envelope.Payload.Coordination.BackupRoot;
        var backupOperationRoot = Path.Combine(backupRoot,
            request.OperationId.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.OperationId.Nonce.ToString("N"));
        Directory.CreateDirectory(backupOperationRoot);
        foreach (var path in new[] { backupRoot, Path.GetDirectoryName(backupOperationRoot)!, backupOperationRoot })
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var unexpectedBackup = Path.Combine(backupOperationRoot, "unexpected.backup");
        File.WriteAllText(unexpectedBackup, "unexpected");
        var cleanup = new JournaledNoEffectCleanup(
            restarted.Ledger, restarted.Store, new LinuxStagingCleanupBoundary(stagingRoot));

        cleanup.Cleanup(request.RepositoryId, request.OperationId, successor)
            .Match(_ => throw new InvalidOperationException("The unjournaled backup was ignored."),
                error => error.Kind)
            .ShouldBe(NoEffectCleanupErrorKind.ArtifactUnavailable);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.Exists(unexpectedBackup).ShouldBeTrue();
        File.Exists(artifact.ArtifactPath).ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
        File.Delete(unexpectedBackup);

        var result = cleanup.Cleanup(request.RepositoryId, request.OperationId, successor);

        var reconciled = result.Match(value => value, error => throw new IOException(error.Message));
        reconciled.CurrentState.ShouldBe(OperationLifecycleState.RejectedUnsafeCoordination);
        reconciled.CleanupJournal.Select(entry => entry.Kind).ShouldBe([
            OperationCleanupJournalKind.InventoryPending,
            OperationCleanupJournalKind.ArtifactRemoved,
            OperationCleanupJournalKind.CleanupVerified
        ]);
        File.Exists(artifact.ArtifactPath).ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
        restarted.Ledger.Load(request.RepositoryId, request.OperationId).IsOk.ShouldBeTrue();
    }

    [Fact]
    public async Task successor_cleans_staging_and_backup_without_changing_repository_original()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        var backupRoot = plan.Envelope.Payload.Coordination.BackupRoot;
        foreach (var root in new[] { stagingRoot, backupRoot })
        {
            Directory.CreateDirectory(root);
            File.SetUnixFileMode(root,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var first = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(request.RepositoryId, request.OperationId, first)
            .Match(value => value, error => throw new IOException(error.Message));
        var stage = (await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, first))
            .Match(value => value, error => throw new IOException(error.Message));
        var backup = (await new JournaledBackupArtifactWriter(
                fixture.Ledger, new LinuxBackupArtifactBoundary(backupRoot))
            .BackupOriginalAsync(plan, request.OperationId, 1, activity, first))
            .Match(value => value, error => throw new IOException(error.Message));
        first.Dispose();
        var restarted = fixture.RestartedComponents();
        using var successor = await restarted.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);

        var result = new JournaledNoEffectCleanup(
                restarted.Ledger, restarted.Store,
                new LinuxStagingCleanupBoundary(stagingRoot),
                new LinuxBackupCleanupBoundary(backupRoot))
            .Cleanup(request.RepositoryId, request.OperationId, successor);

        var cleaned = result.Match(value => value, error => throw new IOException(error.Message));
        cleaned.CurrentState.ShouldBe(OperationLifecycleState.RejectedUnsafeCoordination);
        cleaned.CleanupJournal[0].Artifacts.Select(artifact => artifact.Kind).ShouldBe([
            OperationCleanupArtifactKind.Staging,
            OperationCleanupArtifactKind.Backup
        ]);
        cleaned.CleanupJournal[^1].Kind.ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        File.Exists(stage.ArtifactPath).ShouldBeFalse();
        File.Exists(backup.ArtifactPath).ShouldBeFalse();
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task successor_preserves_pin_when_guard_acquisition_was_attempted()
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
        var first = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(request.RepositoryId, request.OperationId, first)
            .Match(value => value, error => throw new IOException(error.Message));
        var artifact = (await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, first))
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.JournalGuardIntent(request, activity, first);
        first.Dispose();
        var restarted = fixture.RestartedComponents();
        using var successor = await restarted.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);

        var result = new JournaledNoEffectCleanup(
                restarted.Ledger, restarted.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
                new LinuxBackupCleanupBoundary(plan.Envelope.Payload.Coordination.BackupRoot))
            .Cleanup(request.RepositoryId, request.OperationId, successor);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => throw new InvalidOperationException("Expected unresolved guard."),
                error => error.Kind)
            .ShouldBe(NoEffectCleanupErrorKind.UnresolvedGuard);
        fixture.LoadOperation(request).CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.Exists(artifact.ArtifactPath).ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task successor_drains_cancelled_activity_after_confirmed_guard_inactivity()
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
        var first = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(request.RepositoryId, request.OperationId, first)
            .Match(value => value, error => throw new IOException(error.Message));
        var artifact = (await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, first))
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.JournalGuardIntent(request, activity, first);
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.AppendGuardConfirmedInactive(request.RepositoryId, request.OperationId,
                activity, first)
            .IsOk.ShouldBeTrue();
        first.Dispose();
        var restarted = fixture.RestartedComponents();
        using var successor = await restarted.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);

        var result = new JournaledNoEffectCleanup(
                restarted.Ledger, restarted.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
                new LinuxBackupCleanupBoundary(plan.Envelope.Payload.Coordination.BackupRoot))
            .Cleanup(request.RepositoryId, request.OperationId, successor);

        var reconciled = result.Match(value => value, error => throw new IOException(error.Message));
        reconciled.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        reconciled.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        reconciled.CleanupJournal[0].Artifacts.ShouldHaveSingleItem().ArtifactPath
            .ShouldBe(artifact.ArtifactPath);
        reconciled.CleanupJournal[^1].Kind.ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        File.Exists(artifact.ArtifactPath).ShouldBeFalse();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task successor_cleans_creation_admission_when_artifact_was_never_created()
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
        var first = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(request.RepositoryId, request.OperationId, first)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendStagingIntent(plan, request.OperationId, 1, activity, first)
            .IsOk.ShouldBeTrue();
        var creation = fixture.Ledger.AppendStagingCreationAdmission(
                plan, request.OperationId, 1, activity, first)
            .Match(value => value, error => throw new IOException(error.Message));
        first.Dispose();
        var restarted = fixture.RestartedComponents();
        using var successor = await restarted.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);

        var result = new JournaledNoEffectCleanup(
                restarted.Ledger, restarted.Store, new LinuxStagingCleanupBoundary(stagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, successor);

        var reconciled = result.Match(value => value, error => throw new IOException(error.Message));
        reconciled.CleanupJournal[0].Artifacts.ShouldHaveSingleItem();
        reconciled.CleanupJournal[0].Artifacts[0].CreationToken
            .ShouldBe(creation.CreationToken!.Value);
        reconciled.CleanupJournal[^1].Kind.ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
        restarted.Ledger.Load(request.RepositoryId, request.OperationId).IsOk.ShouldBeTrue();

        var recordPath = DurableOperationLedger.GetRecordPath(
            fixture.LedgerRoot, request.RepositoryId, request.OperationId);
        var json = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        json.Remove("guardJournal");
        File.WriteAllText(recordPath, json.ToJsonString());
        restarted.Ledger.Load(request.RepositoryId, request.OperationId)
            .Match(_ => throw new InvalidOperationException("Missing guard history was accepted."),
                error => error.Kind)
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
    }

    [Fact]
    public async Task failed_reconciliation_flush_keeps_artifact_and_pin_until_retry()
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
        var first = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(request.RepositoryId, request.OperationId, first)
            .Match(value => value, error => throw new IOException(error.Message));
        var staged = await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, first);
        var artifact = staged.Match(value => value, error => throw new IOException(error.Message));
        first.Dispose();
        using var successor = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var failing = new JournaledNoEffectCleanup(
            fixture.CreateLedger(new FailNthDirectoryFlush(1)), fixture.Store,
            new LinuxStagingCleanupBoundary(stagingRoot));

        var interrupted = failing.Cleanup(request.RepositoryId, request.OperationId, successor);

        interrupted.IsErr.ShouldBeTrue();
        interrupted.Match(_ => throw new InvalidOperationException("Expected storage failure."),
                error => error.Kind)
            .ShouldBe(NoEffectCleanupErrorKind.LedgerUnavailable);
        File.Exists(artifact.ArtifactPath).ShouldBeTrue();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        fixture.LoadOperation(request).PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();

        new JournaledNoEffectCleanup(fixture.CreatePlainLedger(), fixture.Store,
                new LinuxStagingCleanupBoundary(stagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, successor)
            .IsOk.ShouldBeTrue();
        File.Exists(artifact.ArtifactPath).ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task failed_terminal_confirmation_keeps_artifact_and_pin_until_retry()
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
        var first = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(request.RepositoryId, request.OperationId, first)
            .Match(value => value, error => throw new IOException(error.Message));
        var artifact = (await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, first))
            .Match(value => value, error => throw new IOException(error.Message));
        first.Dispose();
        using var successor = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var failing = new JournaledNoEffectCleanup(
            fixture.CreateLedger(new FailNthDirectoryFlush(2)), fixture.Store,
            new LinuxStagingCleanupBoundary(stagingRoot));

        var interrupted = failing.Cleanup(request.RepositoryId, request.OperationId, successor);

        interrupted.Match(_ => throw new InvalidOperationException("Confirmation unexpectedly succeeded."),
                error => error.Kind)
            .ShouldBe(NoEffectCleanupErrorKind.LedgerUnavailable);
        fixture.LoadOperation(request).CurrentState
            .ShouldBe(OperationLifecycleState.RejectedUnsafeCoordination);
        fixture.LoadOperation(request).CleanupJournal[^1].Kind
            .ShouldBe(OperationCleanupJournalKind.InventoryPending);
        File.Exists(artifact.ArtifactPath).ShouldBeTrue();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();

        new JournaledNoEffectCleanup(fixture.CreatePlainLedger(), fixture.Store,
                new LinuxStagingCleanupBoundary(stagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, successor)
            .IsOk.ShouldBeTrue();
        File.Exists(artifact.ArtifactPath).ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task original_holder_cannot_reconcile_its_own_active_activity()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        fixture.Ledger.BeginPreEffectActivity(request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();

        var result = new JournaledNoEffectCleanup(
                fixture.Ledger, fixture.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => throw new InvalidOperationException("Expected active activity."),
                error => error.Kind)
            .ShouldBe(NoEffectCleanupErrorKind.NotReady);
        fixture.LoadOperation(request).PreEffectActivity.ActiveAdmissions.ShouldHaveSingleItem();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task successor_reconciles_owner_claim_interrupted_before_activity_admission()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var first = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        fixture.Ledger.ClaimApplyOwner(request.RepositoryId, request.OperationId, first,
                plan.Key.PlanHash)
            .IsOk.ShouldBeTrue();
        first.Dispose();
        var restarted = fixture.RestartedComponents();
        using var successor = await restarted.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);

        var result = new JournaledNoEffectCleanup(
                restarted.Ledger, restarted.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
                new LinuxBackupCleanupBoundary(plan.Envelope.Payload.Coordination.BackupRoot))
            .Cleanup(request.RepositoryId, request.OperationId, successor);

        var reconciled = result.Match(value => value, error => throw new IOException(error.Message));
        reconciled.CurrentState.ShouldBe(OperationLifecycleState.RejectedUnsafeCoordination);
        reconciled.PreEffectActivity.IsClosed.ShouldBeTrue();
        reconciled.CleanupJournal[^1].Kind.ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
        restarted.Ledger.Load(request.RepositoryId, request.OperationId).IsOk.ShouldBeTrue();
    }

    [Fact]
    public async Task successor_keeps_missing_guard_history_unresolved_and_pinned()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var first = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        fixture.Ledger.BeginPreEffectActivity(request.RepositoryId, request.OperationId, first)
            .IsOk.ShouldBeTrue();
        var recordPath = DurableOperationLedger.GetRecordPath(
            fixture.LedgerRoot, request.RepositoryId, request.OperationId);
        var json = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        json.Remove("guardJournal");
        File.WriteAllText(recordPath, json.ToJsonString());
        first.Dispose();
        var restarted = fixture.RestartedComponents();
        using var successor = await restarted.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);

        var result = new JournaledNoEffectCleanup(
                restarted.Ledger, restarted.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, successor);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => throw new InvalidOperationException("Expected missing guard proof."),
                error => error.Kind)
            .ShouldBe(NoEffectCleanupErrorKind.UnresolvedGuard);
        fixture.LoadOperation(request).PreEffectActivity.ActiveAdmissions.ShouldHaveSingleItem();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task no_artifact_reconciliation_requires_both_plan_bound_control_roots()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var first = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        fixture.Ledger.BeginPreEffectActivity(request.RepositoryId, request.OperationId, first)
            .IsOk.ShouldBeTrue();
        first.Dispose();
        var restarted = fixture.RestartedComponents();
        using var successor = await restarted.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        var backupRoot = plan.Envelope.Payload.Coordination.BackupRoot;
        var unbound = new JournaledNoEffectCleanup(
            restarted.Ledger, restarted.Store,
            new LinuxStagingCleanupBoundary(stagingRoot),
            new LinuxBackupCleanupBoundary(Path.Combine(backupRoot, "other")));

        var missing = unbound.Cleanup(request.RepositoryId, request.OperationId, successor);

        missing.IsErr.ShouldBeTrue();
        missing.Match(_ => throw new InvalidOperationException("Expected mismatched backup root."),
                error => error.Kind)
            .ShouldBe(NoEffectCleanupErrorKind.UnsafeRoot);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();

        var operationRoot = Path.Combine(backupRoot,
            request.OperationId.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.OperationId.Nonce.ToString("N"));
        Directory.CreateDirectory(operationRoot);
        foreach (var path in new[] { backupRoot, Path.GetDirectoryName(operationRoot)!, operationRoot })
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var unknown = Path.Combine(operationRoot, "unknown.backup");
        File.WriteAllText(unknown, "unknown");
        var bound = new JournaledNoEffectCleanup(
            restarted.Ledger, restarted.Store,
            new LinuxStagingCleanupBoundary(stagingRoot),
            new LinuxBackupCleanupBoundary(backupRoot));

        bound.Cleanup(request.RepositoryId, request.OperationId, successor)
            .Match(_ => throw new InvalidOperationException("Unknown backup was ignored."),
                error => error.Kind)
            .ShouldBe(NoEffectCleanupErrorKind.ArtifactUnavailable);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.Delete(unknown);
        bound.Cleanup(request.RepositoryId, request.OperationId, successor)
            .IsOk.ShouldBeTrue();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task missing_exact_plan_blocks_no_artifact_cleanup_until_plan_returns()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var first = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        fixture.Ledger.BeginPreEffectActivity(request.RepositoryId, request.OperationId, first)
            .IsOk.ShouldBeTrue();
        first.Dispose();
        var restarted = fixture.RestartedComponents();
        using var successor = await restarted.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var cleanup = new JournaledNoEffectCleanup(
            restarted.Ledger, restarted.Store,
            new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
            new LinuxBackupCleanupBoundary(plan.Envelope.Payload.Coordination.BackupRoot));
        var planPath = Directory.GetFiles(fixture.PlanStoreRoot, "*.plan", SearchOption.AllDirectories)
            .ShouldHaveSingleItem();
        var withheldPath = planPath + ".withheld";

        File.Move(planPath, withheldPath);
        try
        {
            var result = cleanup.Cleanup(request.RepositoryId, request.OperationId, successor);
            result.Match(_ => throw new InvalidOperationException("Missing plan was accepted."),
                    error => error.Kind)
                .ShouldBe(NoEffectCleanupErrorKind.PlanUnavailable);
            fixture.LoadOperation(request).CleanupJournal[^1].Kind
                .ShouldBe(OperationCleanupJournalKind.InventoryPending);
            File.Exists(fixture.TargetPath).ShouldBeFalse();
        }
        finally
        {
            File.Move(withheldPath, planPath);
        }

        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        cleanup.Cleanup(request.RepositoryId, request.OperationId, successor)
            .IsOk.ShouldBeTrue();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task unpinned_retained_plan_cannot_authorize_interrupted_cleanup()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(pin: false);
        var plan = fixture.StoredPlan!;
        var first = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        fixture.Ledger.BeginPreEffectActivity(request.RepositoryId, request.OperationId, first)
            .IsOk.ShouldBeTrue();
        first.Dispose();
        var restarted = fixture.RestartedComponents();
        using var successor = await restarted.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);

        var result = new JournaledNoEffectCleanup(
                restarted.Ledger, restarted.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, successor);

        result.Match(_ => throw new InvalidOperationException("Unpinned plan authorized cleanup."),
                error => error.Kind)
            .ShouldBe(NoEffectCleanupErrorKind.PlanUnavailable);
        fixture.LoadOperation(request).CleanupJournal[^1].Kind
            .ShouldBe(OperationCleanupJournalKind.InventoryPending);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task restarted_owner_finishes_terminal_no_effect_cleanup_under_a_new_reservation(
        bool guardRejected)
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        var backupRoot = plan.Envelope.Payload.Coordination.BackupRoot;
        var broker = new SimulatedBroker(fixture)
        {
            DeclineAfterInspect = guardRejected,
            CancelDuringPreflight = !guardRejected
        };
        var attempt = await fixture.CreateAuthority(broker).ApplyAsync(request);
        attempt.IsErr.ShouldBe(guardRejected);
        var terminal = fixture.LoadOperation(request);
        terminal.CurrentState.ShouldBe(guardRejected
            ? OperationLifecycleState.RejectedUnsafeCoordination
            : OperationLifecycleState.CancelledNoEffect);
        var artifacts = terminal.CleanupJournal[0].Artifacts;
        artifacts.Select(artifact => artifact.Kind).ShouldBe([
            OperationCleanupArtifactKind.Staging,
            OperationCleanupArtifactKind.Backup
        ]);

        var restarted = fixture.RestartedComponents();
        using var restartedReservation = await restarted.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        restartedReservation.FenceEpoch.ShouldBeGreaterThan(artifacts[0].FenceEpoch);
        var cleanup = new JournaledNoEffectCleanup(
            restarted.Ledger, restarted.Store,
            new LinuxStagingCleanupBoundary(stagingRoot),
            new LinuxBackupCleanupBoundary(backupRoot));

        var cleaned = cleanup.Cleanup(request.RepositoryId, request.OperationId,
                restartedReservation)
            .Match(value => value, error => throw new IOException(error.Message));

        cleaned.CleanupJournal.Select(entry => entry.Kind).ShouldBe([
            OperationCleanupJournalKind.InventoryPending,
            OperationCleanupJournalKind.ArtifactRemoved,
            OperationCleanupJournalKind.ArtifactRemoved,
            OperationCleanupJournalKind.CleanupVerified
        ]);
        artifacts.All(artifact => !File.Exists(artifact.ArtifactPath)).ShouldBeTrue();
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();

        var replay = cleanup.Cleanup(request.RepositoryId, request.OperationId,
                restartedReservation)
            .Match(value => value, error => throw new IOException(error.Message));
        replay.CleanupJournal.Select(entry => entry.Sequence)
            .ShouldBe(cleaned.CleanupJournal.Select(entry => entry.Sequence));
        replay.CleanupJournal.Select(entry => entry.Kind)
            .ShouldBe(cleaned.CleanupJournal.Select(entry => entry.Kind));
        replay.CleanupJournal.SelectMany(entry => entry.Artifacts)
            .ShouldBe(cleaned.CleanupJournal.SelectMany(entry => entry.Artifacts));
    }

    [Fact]
    public async Task restarted_owner_finishes_backup_only_no_effect_cleanup()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var plan = fixture.StoredPlan!;
        var backupRoot = plan.Envelope.Payload.Coordination.BackupRoot;
        Directory.CreateDirectory(backupRoot);
        File.SetUnixFileMode(backupRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        OperationCleanupArtifact artifact;
        using (var original = await fixture.Reservations.AcquireAsync(
                   plan.Envelope.Payload.Repository, CancellationToken.None))
        {
            var activity = fixture.Ledger.BeginPreEffectActivity(
                    request.RepositoryId, request.OperationId, original)
                .Match(value => value, error => throw new IOException(error.Message));
            (await new JournaledBackupArtifactWriter(
                    fixture.Ledger, new LinuxBackupArtifactBoundary(backupRoot))
                .BackupOriginalAsync(plan, request.OperationId, 1, activity, original))
                .IsOk.ShouldBeTrue();
            fixture.Ledger.CompletePreEffectActivity(
                    request.RepositoryId, request.OperationId, activity, original)
                .IsOk.ShouldBeTrue();
            fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
                .IsOk.ShouldBeTrue();
            var pending = fixture.LoadOperation(request);
            pending.StagingJournal.ShouldBeEmpty();
            artifact = pending.CleanupJournal[0].Artifacts.ShouldHaveSingleItem();
            artifact.Kind.ShouldBe(OperationCleanupArtifactKind.Backup);
        }

        var restarted = fixture.RestartedComponents();
        using var successor = await restarted.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        successor.FenceEpoch.ShouldBeGreaterThan(artifact.FenceEpoch);
        var cleaned = new JournaledNoEffectCleanup(
                restarted.Ledger, restarted.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
                new LinuxBackupCleanupBoundary(backupRoot))
            .Cleanup(request.RepositoryId, request.OperationId, successor)
            .Match(value => value, error => throw new IOException(error.Message));

        cleaned.CleanupJournal[^1].Kind.ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        File.Exists(artifact.ArtifactPath).ShouldBeFalse();
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task restarted_owner_rejects_backup_artifacts_from_mixed_reservations()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(twoEffects: true, replacement: true,
            secondReplacement: true);
        var plan = fixture.StoredPlan!;
        var backupRoot = plan.Envelope.Payload.Coordination.BackupRoot;
        Directory.CreateDirectory(backupRoot);
        File.SetUnixFileMode(backupRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var writer = new JournaledBackupArtifactWriter(
            fixture.Ledger, new LinuxBackupArtifactBoundary(backupRoot));
        using (var first = await fixture.Reservations.AcquireAsync(
                   plan.Envelope.Payload.Repository, CancellationToken.None))
        {
            var activity = fixture.Ledger.BeginPreEffectActivity(
                    request.RepositoryId, request.OperationId, first)
                .Match(value => value, error => throw new IOException(error.Message));
            (await writer.BackupOriginalAsync(plan, request.OperationId, 1, activity, first))
                .IsOk.ShouldBeTrue();
            fixture.Ledger.CompletePreEffectActivity(
                    request.RepositoryId, request.OperationId, activity, first)
                .IsOk.ShouldBeTrue();
        }
        using (var second = await fixture.Reservations.AcquireAsync(
                   plan.Envelope.Payload.Repository, CancellationToken.None))
        {
            var activity = fixture.Ledger.BeginPreEffectActivity(
                    request.RepositoryId, request.OperationId, second)
                .Match(value => value, error => throw new IOException(error.Message));
            (await writer.BackupOriginalAsync(plan, request.OperationId, 2, activity, second))
                .IsOk.ShouldBeTrue();
            fixture.Ledger.CompletePreEffectActivity(
                    request.RepositoryId, request.OperationId, activity, second)
                .IsOk.ShouldBeTrue();
            fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
                .IsOk.ShouldBeTrue();
        }

        var pending = fixture.LoadOperation(request);
        pending.StagingJournal.ShouldBeEmpty();
        var artifacts = pending.CleanupJournal[0].Artifacts;
        artifacts.Select(artifact => artifact.Kind).ShouldBe([
            OperationCleanupArtifactKind.Backup, OperationCleanupArtifactKind.Backup
        ]);
        artifacts[0].FenceEpoch.ShouldBeLessThan(artifacts[1].FenceEpoch);
        var restarted = fixture.RestartedComponents();
        using var successor = await restarted.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var result = new JournaledNoEffectCleanup(
                restarted.Ledger, restarted.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
                new LinuxBackupCleanupBoundary(backupRoot))
            .Cleanup(request.RepositoryId, request.OperationId, successor);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => throw new InvalidOperationException("Mixed backup cleanup unexpectedly succeeded."),
            error => error.Kind).ShouldBe(NoEffectCleanupErrorKind.NotReady);
        artifacts.All(artifact => File.Exists(artifact.ArtifactPath)).ShouldBeTrue();
        fixture.LoadOperation(request).CleanupJournal.ShouldHaveSingleItem();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
        File.ReadAllBytes(Path.Combine(fixture.RepositoryRoot, "src", "Added2.cs"))
            .ShouldBe(fixture.OriginalContent);
    }

    [Fact]
    public async Task foreign_reservation_cannot_resume_terminal_no_effect_cleanup()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var (request, plan, artifact) = await PrepareRejectedStagingAsync(fixture);

        var foreignRoot = Path.Combine(Path.GetDirectoryName(fixture.RepositoryRoot)!,
            "foreign-control");
        var foreignAuthority = new LocalDurableOperationAuthority(foreignRoot, () => fixture.Now);
        var foreignReservations = new RepositoryMutationReservationProvider(
            foreignAuthority.MutationReservationRoot, foreignAuthority);
        using (var earlier = await foreignReservations.AcquireAsync(
                   plan.Envelope.Payload.Repository, CancellationToken.None))
        {
            earlier.FenceEpoch.ShouldBe(artifact.FenceEpoch);
        }
        using var foreign = await foreignReservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        foreign.FenceEpoch.ShouldBeGreaterThan(artifact.FenceEpoch);

        var cleanup = new JournaledNoEffectCleanup(
            fixture.CreatePlainLedger(), fixture.Store,
            new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot));
        var result = cleanup.Cleanup(request.RepositoryId, request.OperationId, foreign);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => throw new InvalidOperationException("Foreign cleanup unexpectedly succeeded."),
            error => error.Kind).ShouldBe(NoEffectCleanupErrorKind.NotReady);
        fixture.LoadOperation(request).CleanupJournal.ShouldHaveSingleItem();
        File.Exists(artifact.ArtifactPath).ShouldBeTrue();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task reused_fence_epoch_cannot_resume_terminal_no_effect_cleanup()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var (request, plan, artifact) = await PrepareRejectedStagingAsync(fixture);
        var epochPath = Directory.GetFiles(fixture.ReservationRoot, "*.epoch")
            .ShouldHaveSingleItem();
        File.Delete(epochPath);

        using var reusedEpoch = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        reusedEpoch.FenceEpoch.ShouldBe(artifact.FenceEpoch);
        reusedEpoch.FenceToken.ShouldNotBe(artifact.ReservationToken);
        var result = new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reusedEpoch);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => throw new InvalidOperationException("Reused epoch cleanup unexpectedly succeeded."),
            error => error.Kind).ShouldBe(NoEffectCleanupErrorKind.NotReady);
        File.Exists(artifact.ArtifactPath).ShouldBeTrue();
        fixture.LoadOperation(request).CleanupJournal.ShouldHaveSingleItem();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task restarted_cleanup_retries_an_unjournaled_artifact_unlink_under_another_reservation()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var (request, plan, artifact) = await PrepareRejectedStagingAsync(fixture);
        using (var interruptedReservation = await fixture.Reservations.AcquireAsync(
                   plan.Envelope.Payload.Repository, CancellationToken.None))
        {
            var interrupted = new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.Store,
                new LinuxStagingCleanupBoundary(
                    plan.Envelope.Payload.Coordination.StagingRoot,
                    new RecordingStagingDurability(failedFlush: 3)));
            interrupted.Cleanup(request.RepositoryId, request.OperationId,
                    interruptedReservation)
                .IsErr.ShouldBeTrue();
        }

        File.Exists(artifact.ArtifactPath).ShouldBeFalse();
        fixture.LoadOperation(request).CleanupJournal.ShouldHaveSingleItem();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();

        using var restartedReservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var cleaned = new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.Store,
                new LinuxStagingCleanupBoundary(
                    plan.Envelope.Payload.Coordination.StagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, restartedReservation)
            .Match(value => value, error => throw new IOException(error.Message));

        cleaned.CleanupJournal.Select(entry => entry.Kind).ShouldBe([
            OperationCleanupJournalKind.InventoryPending,
            OperationCleanupJournalKind.ArtifactRemoved,
            OperationCleanupJournalKind.CleanupVerified
        ]);
        Directory.Exists(Path.GetDirectoryName(artifact.ArtifactPath)!).ShouldBeFalse();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task restarted_cleanup_keeps_a_cancelled_but_active_guard_pinned()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var broker = new SimulatedBroker(fixture)
        {
            CancelDuringPreflight = true,
            RequireActiveAtDisposeAfterCancellation = true,
            FailDisposeAfterCancellation = true
        };
        (await fixture.CreateAuthority(broker).ApplyAsync(request)).IsErr.ShouldBeTrue();
        var terminal = fixture.LoadOperation(request);
        terminal.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        terminal.PreEffectActivity.ActiveAdmissions.ShouldHaveSingleItem();
        var artifact = terminal.CleanupJournal[0].Artifacts.ShouldHaveSingleItem();

        using var restartedReservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var result = new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, restartedReservation);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => throw new InvalidOperationException("Active cleanup unexpectedly succeeded."),
            error => error.Kind).ShouldBe(NoEffectCleanupErrorKind.UnresolvedGuard);
        fixture.LoadOperation(request).CleanupJournal.ShouldHaveSingleItem();
        File.Exists(artifact.ArtifactPath).ShouldBeTrue();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task restarted_cleanup_recovers_when_removal_record_write_fails()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var (request, plan, artifact) = await PrepareRejectedStagingAsync(fixture);

        using (var interruptedReservation = await fixture.Reservations.AcquireAsync(
                   plan.Envelope.Payload.Repository, CancellationToken.None))
        {
            var interrupted = new JournaledNoEffectCleanup(
                fixture.CreateLedger(new FailNthReplace(1)), fixture.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot));
            var result = interrupted.Cleanup(request.RepositoryId, request.OperationId,
                interruptedReservation);
            result.IsErr.ShouldBeTrue();
            result.Match(_ => throw new InvalidOperationException("Cleanup unexpectedly succeeded."),
                error => error.Kind).ShouldBe(NoEffectCleanupErrorKind.LedgerUnavailable);
        }

        File.Exists(artifact.ArtifactPath).ShouldBeFalse();
        fixture.LoadOperation(request).CleanupJournal.ShouldHaveSingleItem();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();

        using var restartedReservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var cleaned = new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, restartedReservation)
            .Match(value => value, error => throw new IOException(error.Message));

        cleaned.CleanupJournal.Select(entry => entry.Kind).ShouldBe([
            OperationCleanupJournalKind.InventoryPending,
            OperationCleanupJournalKind.ArtifactRemoved,
            OperationCleanupJournalKind.CleanupVerified
        ]);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task restarted_cleanup_confirms_empty_roots_after_verification_record_write_fails()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var (request, plan, artifact) = await PrepareRejectedStagingAsync(fixture);

        using (var interruptedReservation = await fixture.Reservations.AcquireAsync(
                   plan.Envelope.Payload.Repository, CancellationToken.None))
        {
            var interrupted = new JournaledNoEffectCleanup(
                fixture.CreateLedger(new FailNthReplace(2)), fixture.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot));
            var result = interrupted.Cleanup(request.RepositoryId, request.OperationId,
                interruptedReservation);
            result.IsErr.ShouldBeTrue();
            result.Match(_ => throw new InvalidOperationException("Cleanup unexpectedly succeeded."),
                error => error.Kind).ShouldBe(NoEffectCleanupErrorKind.LedgerUnavailable);
        }

        Directory.Exists(Path.GetDirectoryName(artifact.ArtifactPath)!).ShouldBeFalse();
        fixture.LoadOperation(request).CleanupJournal.Select(entry => entry.Kind).ShouldBe([
            OperationCleanupJournalKind.InventoryPending,
            OperationCleanupJournalKind.ArtifactRemoved
        ]);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();

        using var restartedReservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var cleaned = new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, restartedReservation)
            .Match(value => value, error => throw new IOException(error.Message));

        cleaned.CleanupJournal[^1].Kind.ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task only_a_local_successor_can_release_a_pin_after_cleanup_was_verified()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var (request, plan, _) = await PrepareRejectedStagingAsync(fixture);
        using (var firstCleanupReservation = await fixture.Reservations.AcquireAsync(
                   plan.Envelope.Payload.Repository, CancellationToken.None))
        {
            var interrupted = new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.CreateStore(new FailPlanReplace()),
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot));
            var result = interrupted.Cleanup(request.RepositoryId, request.OperationId,
                firstCleanupReservation);
            result.IsErr.ShouldBeTrue();
            result.Match(_ => throw new InvalidOperationException("Pin release unexpectedly succeeded."),
                error => error.Kind).ShouldBe(NoEffectCleanupErrorKind.PinReleaseUnavailable);
        }

        fixture.LoadOperation(request).CleanupJournal[^1].Kind
            .ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        fixture.LoadOperation(request).CleanupPinReleased.ShouldBeFalse();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();

        var foreignRoot = Path.Combine(Path.GetDirectoryName(fixture.RepositoryRoot)!,
            "foreign-control");
        var foreignAuthority = new LocalDurableOperationAuthority(foreignRoot, () => fixture.Now);
        var foreignReservations = new RepositoryMutationReservationProvider(
            foreignAuthority.MutationReservationRoot, foreignAuthority);
        using (var earlier = await foreignReservations.AcquireAsync(
                   plan.Envelope.Payload.Repository, CancellationToken.None))
        {
            earlier.FenceEpoch.ShouldBeGreaterThan(0);
        }
        using (var foreign = await foreignReservations.AcquireAsync(
                   plan.Envelope.Payload.Repository, CancellationToken.None))
        {
            var denied = new JournaledNoEffectCleanup(
                    fixture.CreatePlainLedger(), fixture.Store,
                    new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot))
                .Cleanup(request.RepositoryId, request.OperationId, foreign);
            denied.IsErr.ShouldBeTrue();
            denied.Match(_ => throw new InvalidOperationException("Foreign pin release unexpectedly succeeded."),
                error => error.Kind).ShouldBe(NoEffectCleanupErrorKind.NotReady);
        }
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();

        using var restartedReservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.CreateStore(),
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, restartedReservation)
            .IsOk.ShouldBeTrue();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
        fixture.LoadOperation(request).CleanupPinReleased.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task restarted_pin_release_rechecks_verified_cleanup_roots()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var (request, plan, artifact) = await PrepareRejectedStagingAsync(fixture);
        using (var firstCleanupReservation = await fixture.Reservations.AcquireAsync(
                   plan.Envelope.Payload.Repository, CancellationToken.None))
        {
            var interrupted = new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.CreateStore(new FailPlanReplace()),
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot));
            interrupted.Cleanup(request.RepositoryId, request.OperationId,
                    firstCleanupReservation)
                .Match(_ => throw new InvalidOperationException("Pin release unexpectedly succeeded."),
                    error => error.Kind)
                .ShouldBe(NoEffectCleanupErrorKind.PinReleaseUnavailable);
        }

        var operationRoot = Path.GetDirectoryName(artifact.ArtifactPath)!;
        Directory.CreateDirectory(operationRoot);
        File.SetUnixFileMode(Path.GetDirectoryName(operationRoot)!,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(operationRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var unknown = Path.Combine(operationRoot, "unexpected");
        File.WriteAllText(unknown, "unadmitted");

        using var restartedReservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var cleanup = new JournaledNoEffectCleanup(
            fixture.CreatePlainLedger(), fixture.CreateStore(),
            new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot));
        var blocked = cleanup.Cleanup(request.RepositoryId, request.OperationId,
            restartedReservation);

        blocked.IsErr.ShouldBeTrue();
        blocked.Match(_ => throw new InvalidOperationException("Pin release unexpectedly succeeded."),
            error => error.Kind).ShouldBe(NoEffectCleanupErrorKind.ArtifactUnavailable);
        File.Exists(unknown).ShouldBeTrue();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();

        File.Delete(unknown);
        cleanup.Cleanup(request.RepositoryId, request.OperationId, restartedReservation)
            .IsOk.ShouldBeTrue();
        Directory.Exists(operationRoot).ShouldBeFalse();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task missing_plan_after_cleanup_verification_does_not_imply_pin_release()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var (request, plan, _) = await PrepareRejectedStagingAsync(fixture);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var cleanup = new JournaledNoEffectCleanup(
            fixture.CreatePlainLedger(), fixture.CreateStore(new FailPlanReplace()),
            new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot));
        cleanup.Cleanup(request.RepositoryId, request.OperationId, reservation)
            .Match(_ => throw new InvalidOperationException("Pin release unexpectedly succeeded."),
                error => error.Kind)
            .ShouldBe(NoEffectCleanupErrorKind.PinReleaseUnavailable);
        fixture.LoadOperation(request).CleanupJournal[^1].Kind
            .ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        var planPath = Directory.GetFiles(fixture.PlanStoreRoot, "*.plan", SearchOption.AllDirectories)
            .ShouldHaveSingleItem();
        var withheldPath = planPath + ".withheld";

        File.Move(planPath, withheldPath);
        try
        {
            new JournaledNoEffectCleanup(
                    fixture.CreatePlainLedger(), fixture.CreateStore(),
                    new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot))
                .Cleanup(request.RepositoryId, request.OperationId, reservation)
                .Match(_ => throw new InvalidOperationException("Missing pinned plan was accepted."),
                    error => error.Kind)
                .ShouldBe(NoEffectCleanupErrorKind.PlanUnavailable);
            File.Exists(fixture.TargetPath).ShouldBeFalse();
        }
        finally
        {
            File.Move(withheldPath, planPath);
        }

        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.CreateStore(),
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task failed_durable_pin_release_marker_replays_while_plan_is_retained()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var (request, plan, _) = await PrepareRejectedStagingAsync(fixture);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.CreateStore(new FailPlanReplace()),
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).CleanupPinReleased.ShouldBeFalse();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();

        var interrupted = new JournaledNoEffectCleanup(
                fixture.CreateLedger(new FailNthReplace(1)), fixture.CreateStore(),
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation);

        interrupted.Match(_ => throw new InvalidOperationException("Marker failure was ignored."),
                error => error.Kind)
            .ShouldBe(NoEffectCleanupErrorKind.LedgerUnavailable);
        fixture.LoadOperation(request).CleanupJournal[^1].Kind
            .ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        fixture.LoadOperation(request).CleanupPinReleased.ShouldBeFalse();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();

        new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.CreateStore(),
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();
        fixture.LoadOperation(request).CleanupPinReleased.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task restarted_pin_release_rejects_an_unprotected_control_root(
        bool backupRootUnsafe)
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var plan = fixture.StoredPlan!;
        (await fixture.CreateAuthority(
                new SimulatedBroker(fixture) { DeclineAfterInspect = true }).ApplyAsync(request))
            .IsErr.ShouldBeTrue();
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        var backupRoot = plan.Envelope.Payload.Coordination.BackupRoot;
        using (var firstCleanupReservation = await fixture.Reservations.AcquireAsync(
                   plan.Envelope.Payload.Repository, CancellationToken.None))
        {
            new JournaledNoEffectCleanup(
                    fixture.CreatePlainLedger(), fixture.CreateStore(new FailPlanReplace()),
                    new LinuxStagingCleanupBoundary(stagingRoot),
                    new LinuxBackupCleanupBoundary(backupRoot))
                .Cleanup(request.RepositoryId, request.OperationId, firstCleanupReservation)
                .Match(_ => throw new InvalidOperationException("Pin release unexpectedly succeeded."),
                    error => error.Kind)
                .ShouldBe(NoEffectCleanupErrorKind.PinReleaseUnavailable);
        }

        var unsafeRoot = backupRootUnsafe ? backupRoot : stagingRoot;
        File.SetUnixFileMode(unsafeRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead);
        using var restartedReservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var cleanup = new JournaledNoEffectCleanup(
            fixture.CreatePlainLedger(), fixture.CreateStore(),
            new LinuxStagingCleanupBoundary(stagingRoot),
            new LinuxBackupCleanupBoundary(backupRoot));
        var blocked = cleanup.Cleanup(request.RepositoryId, request.OperationId,
            restartedReservation);

        blocked.IsErr.ShouldBeTrue();
        blocked.Match(_ => throw new InvalidOperationException("Unsafe cleanup unexpectedly succeeded."),
            error => error.Kind).ShouldBe(NoEffectCleanupErrorKind.UnsafeRoot);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);

        File.SetUnixFileMode(unsafeRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        cleanup.Cleanup(request.RepositoryId, request.OperationId, restartedReservation)
            .IsOk.ShouldBeTrue();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task admitted_original_backup_is_verified_and_cleaned_after_no_effect_cancellation()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var plan = fixture.StoredPlan!;
        var backupRoot = plan.Envelope.Payload.Coordination.BackupRoot;
        Directory.CreateDirectory(backupRoot);
        File.SetUnixFileMode(backupRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        var verified = (await new JournaledBackupArtifactWriter(
                fixture.Ledger, new LinuxBackupArtifactBoundary(backupRoot))
            .BackupOriginalAsync(plan, request.OperationId, 1, activity, reservation))
            .Match(value => value, error => throw new IOException(error.Message));

        verified.Kind.ShouldBe(OperationBackupJournalKind.Verified);
        verified.ObservedHash.ShouldBe(Hash(fixture.OriginalContent));
        File.ReadAllBytes(verified.ArtifactPath).ShouldBe(fixture.OriginalContent);
        fixture.LoadOperation(request).BackupJournal.Select(entry => entry.Kind).ShouldBe([
            OperationBackupJournalKind.Intent,
            OperationBackupJournalKind.CreationAdmitted,
            OperationBackupJournalKind.Verified
        ]);
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();

        var cleaned = new JournaledNoEffectCleanup(
                fixture.Ledger, fixture.Store,
                new LinuxStagingCleanupBoundary(
                    plan.Envelope.Payload.Coordination.StagingRoot),
                new LinuxBackupCleanupBoundary(backupRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        cleaned.CleanupJournal.Select(entry => entry.Kind).ShouldBe([
            OperationCleanupJournalKind.InventoryPending,
            OperationCleanupJournalKind.ArtifactRemoved,
            OperationCleanupJournalKind.CleanupVerified
        ]);
        File.Exists(verified.ArtifactPath).ShouldBeFalse();
        Directory.Exists(Path.GetDirectoryName(verified.ArtifactPath)!).ShouldBeFalse();
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task backup_intent_only_cleanup_proves_absence_without_creating_a_root()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var plan = fixture.StoredPlan!;
        var backupRoot = plan.Envelope.Payload.Coordination.BackupRoot;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendBackupIntent(plan, request.OperationId, 1, activity, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        var cancelled = fixture.Ledger.CancelBeforeFirstEffect(
                request.RepositoryId, request.OperationId)
            .Match(value => value, error => throw new IOException(error.Message));
        cancelled.CleanupJournal[0].Artifacts.ShouldBeEmpty();
        var uncertainAbsence = new RecordingStagingDurability(failedFlush: 1);
        new JournaledNoEffectCleanup(
                fixture.Ledger, fixture.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
                new LinuxBackupCleanupBoundary(backupRoot, uncertainAbsence))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsErr.ShouldBeTrue();
        uncertainAbsence.Flushes.ShouldBe(["directory"]);
        fixture.LoadOperation(request).CleanupJournal.ShouldHaveSingleItem();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();

        new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
                new LinuxBackupCleanupBoundary(backupRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();
        fixture.LoadOperation(request).CleanupJournal[^1].Kind
            .ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        Directory.Exists(backupRoot).ShouldBeFalse();
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task interrupted_backup_creation_remains_in_inventory_until_the_original_activity_drains(
        int failedFlush)
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var plan = fixture.StoredPlan!;
        var backupRoot = plan.Envelope.Payload.Coordination.BackupRoot;
        Directory.CreateDirectory(backupRoot);
        File.SetUnixFileMode(backupRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var interrupted = await new JournaledBackupArtifactWriter(
                fixture.Ledger, new LinuxBackupArtifactBoundary(
                    backupRoot, new RecordingStagingDurability(failedFlush)))
            .BackupOriginalAsync(plan, request.OperationId, 1, activity, reservation);
        interrupted.IsErr.ShouldBeTrue();
        var admitted = fixture.LoadOperation(request).BackupJournal[^1];
        admitted.Kind.ShouldBe(OperationBackupJournalKind.CreationAdmitted);
        if (failedFlush == 4)
            File.ReadAllBytes(admitted.ArtifactPath).ShouldBe(fixture.OriginalContent);
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();
        var pending = fixture.LoadOperation(request);
        pending.CleanupJournal[0].Artifacts.ShouldHaveSingleItem();
        pending.CleanupJournal[0].Artifacts[0].Kind.ShouldBe(OperationCleanupArtifactKind.Backup);
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsErr.ShouldBeTrue();
        new JournaledNoEffectCleanup(
                fixture.Ledger, fixture.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
                new LinuxBackupCleanupBoundary(backupRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsErr.ShouldBeTrue();

        var verified = await new JournaledBackupArtifactWriter(
                fixture.CreatePlainLedger(), new LinuxBackupArtifactBoundary(backupRoot))
            .BackupOriginalAsync(plan, request.OperationId, 1, activity, reservation);
        verified.IsOk.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
                new LinuxBackupCleanupBoundary(backupRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
        File.Exists(admitted.ArtifactPath).ShouldBeFalse();
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
    }

    [Theory]
    [InlineData("changed-bytes")]
    [InlineData("symlink")]
    [InlineData("hard-link")]
    public async Task backup_cleanup_refuses_changed_or_redirected_original_bytes(string alteration)
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var plan = fixture.StoredPlan!;
        var backupRoot = plan.Envelope.Payload.Coordination.BackupRoot;
        Directory.CreateDirectory(backupRoot);
        File.SetUnixFileMode(backupRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var backup = (await new JournaledBackupArtifactWriter(
                fixture.Ledger, new LinuxBackupArtifactBoundary(backupRoot))
            .BackupOriginalAsync(plan, request.OperationId, 1, activity, reservation))
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();
        var important = Path.Combine(fixture.RepositoryRoot, "src", "Important.cs");
        File.WriteAllText(important, "class Important;\n");
        if (alteration == "changed-bytes")
            File.WriteAllText(backup.ArtifactPath, "changed\n");
        else if (alteration == "symlink")
        {
            File.Delete(backup.ArtifactPath);
            File.CreateSymbolicLink(backup.ArtifactPath, important);
        }
        else
        {
            Link(backup.ArtifactPath, Path.Combine(backupRoot, "backup-alias"))
                .ShouldBe(0);
        }

        new JournaledNoEffectCleanup(
                fixture.Ledger, fixture.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
                new LinuxBackupCleanupBoundary(backupRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).CleanupJournal.ShouldHaveSingleItem();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.ReadAllText(important).ShouldBe("class Important;\n");
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
    }

    [Fact]
    public async Task staging_and_backup_share_one_no_effect_inventory_and_cleanup_fence()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var plan = fixture.StoredPlan!;
        var stagingRoot = plan.Envelope.Payload.Coordination.StagingRoot;
        var backupRoot = plan.Envelope.Payload.Coordination.BackupRoot;
        foreach (var root in new[] { stagingRoot, backupRoot })
        {
            Directory.CreateDirectory(root);
            File.SetUnixFileMode(root,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var staged = (await new JournaledStagingArtifactWriter(
                fixture.Ledger, new LinuxStagingArtifactBoundary(stagingRoot))
            .StageReplacementAsync(plan, request.OperationId, 1, activity, reservation))
            .Match(value => value, error => throw new IOException(error.Message));
        var backup = (await new JournaledBackupArtifactWriter(
                fixture.Ledger, new LinuxBackupArtifactBoundary(backupRoot))
            .BackupOriginalAsync(plan, request.OperationId, 1, activity, reservation))
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        var cancelled = fixture.Ledger.CancelBeforeFirstEffect(
                request.RepositoryId, request.OperationId)
            .Match(value => value, error => throw new IOException(error.Message));
        cancelled.CleanupJournal[0].Artifacts.Select(artifact => artifact.Kind)
            .ShouldBe([OperationCleanupArtifactKind.Staging, OperationCleanupArtifactKind.Backup]);

        var cleaned = new JournaledNoEffectCleanup(
                fixture.Ledger, fixture.Store,
                new LinuxStagingCleanupBoundary(stagingRoot),
                new LinuxBackupCleanupBoundary(backupRoot))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));

        cleaned.CleanupJournal.Select(entry => entry.Kind).ShouldBe([
            OperationCleanupJournalKind.InventoryPending,
            OperationCleanupJournalKind.ArtifactRemoved,
            OperationCleanupJournalKind.ArtifactRemoved,
            OperationCleanupJournalKind.CleanupVerified
        ]);
        File.Exists(staged.ArtifactPath).ShouldBeFalse();
        File.Exists(backup.ArtifactPath).ShouldBeFalse();
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task verified_backup_replay_rejects_missing_or_changed_original_bytes(
        bool removeArtifact)
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var plan = fixture.StoredPlan!;
        var backupRoot = plan.Envelope.Payload.Coordination.BackupRoot;
        Directory.CreateDirectory(backupRoot);
        File.SetUnixFileMode(backupRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var verified = (await new JournaledBackupArtifactWriter(
                fixture.Ledger, new LinuxBackupArtifactBoundary(backupRoot))
            .BackupOriginalAsync(plan, request.OperationId, 1, activity, reservation))
            .Match(value => value, error => throw new IOException(error.Message));
        var journal = fixture.LoadOperation(request).BackupJournal;
        if (removeArtifact)
            File.Delete(verified.ArtifactPath);
        else
            File.WriteAllBytes(verified.ArtifactPath,
                Enumerable.Repeat((byte)'x', fixture.OriginalContent.Length).ToArray());

        var replay = await new JournaledBackupArtifactWriter(
                fixture.CreatePlainLedger(), new LinuxBackupArtifactBoundary(backupRoot))
            .BackupOriginalAsync(plan, request.OperationId, 1, activity, reservation);

        replay.IsErr.ShouldBeTrue();
        replay.Match(_ => throw new ShouldAssertException("Invalid backup replay succeeded."),
            error => error.Kind).ShouldBe(BackupArtifactErrorKind.ArtifactUnavailable);
        fixture.LoadOperation(request).BackupJournal.ShouldBe(journal);
        if (removeArtifact)
            File.Exists(verified.ArtifactPath).ShouldBeFalse();
        else
            File.ReadAllBytes(verified.ArtifactPath)
                .ShouldBe(Enumerable.Repeat((byte)'x', fixture.OriginalContent.Length).ToArray());
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
    }

    [Fact]
    public async Task a_new_reservation_cannot_replay_an_admitted_backup()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var plan = fixture.StoredPlan!;
        var backupRoot = plan.Envelope.Payload.Coordination.BackupRoot;
        Directory.CreateDirectory(backupRoot);
        File.SetUnixFileMode(backupRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var previousReservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, previousReservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var verified = (await new JournaledBackupArtifactWriter(
                fixture.Ledger, new LinuxBackupArtifactBoundary(backupRoot))
            .BackupOriginalAsync(plan, request.OperationId, 1, activity, previousReservation))
            .Match(value => value, error => throw new IOException(error.Message));
        var journal = fixture.LoadOperation(request).BackupJournal;
        previousReservation.Dispose();
        using var newReservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);

        var replay = await new JournaledBackupArtifactWriter(
                fixture.CreatePlainLedger(), new LinuxBackupArtifactBoundary(backupRoot))
            .BackupOriginalAsync(plan, request.OperationId, 1, activity, newReservation);

        replay.IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).BackupJournal.ShouldBe(journal);
        File.ReadAllBytes(verified.ArtifactPath).ShouldBe(fixture.OriginalContent);
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
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
            fixture.CreateLedger(new FailDirectoryFlushFrom(3)),
            fixture.Store,
            new LinuxStagingCleanupBoundary(stagingRoot));

        failing.Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).CleanupJournal[^1].Kind
            .ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        failing.Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsErr.ShouldBeTrue();
        new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.Store,
                new LinuxStagingCleanupBoundary(stagingRoot),
                new LinuxBackupCleanupBoundary(Path.Combine(
                    plan.Envelope.Payload.Coordination.BackupRoot, "other")))
            .Cleanup(request.RepositoryId, request.OperationId, reservation)
            .Match(_ => throw new InvalidOperationException("Replay accepted the wrong backup root."),
                error => error.Kind)
            .ShouldBe(NoEffectCleanupErrorKind.UnsafeRoot);
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
        fixture.Now = fixture.Now.AddDays(2);
        cleanup.Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();
        fixture.LoadOperation(request).CleanupPinReleased.ShouldBeTrue();
        fixture.Store.CollectGarbage().DeletedPlans.ShouldBe(1);

        cleanup.Cleanup(request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();
        fixture.LoadOperation(request).CleanupJournal[^1].Kind
            .ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        fixture.LoadOperation(request).CleanupPinReleased.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public void restart_rejects_pin_release_marker_without_verified_cleanup()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var recordPath = DurableOperationLedger.GetRecordPath(
            fixture.LedgerRoot, request.RepositoryId, request.OperationId);
        var json = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        json["cleanupPinReleased"] = true;
        File.WriteAllText(recordPath, json.ToJsonString());

        fixture.CreatePlainLedger().Load(request.RepositoryId, request.OperationId)
            .Match(_ => throw new InvalidOperationException("Unverified release marker was accepted."),
                error => error.Kind)
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
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
        secondReservation.Dispose();
        using var successor = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        successor.FenceEpoch.ShouldBeGreaterThan(secondReservation.FenceEpoch);
        new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.Store,
                new LinuxStagingCleanupBoundary(stagingRoot))
            .Cleanup(request.RepositoryId, request.OperationId, successor)
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
        fixture.JournalGuardIntent(request, admission, reservation);
        var intent = fixture.Ledger.BeginApplyingWithFirstEffectIntent(
            plan, request.OperationId, admission, reservation,
            OperationGuardTokenSet.Expected(plan.Envelope.Payload));

        intent.IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).CurrentState.ShouldBe(OperationLifecycleState.Prepared);
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
    [InlineData("sequence")]
    [InlineData("reservation")]
    [InlineData("orphan-intent")]
    [InlineData("release-without-attempt")]
    [InlineData("duplicate-marker")]
    [InlineData("untracked-admission")]
    [InlineData("missing-event")]
    [InlineData("no-admission-history")]
    public async Task restart_rejects_inconsistent_guard_history(string tampering)
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.JournalGuardIntent(request, activity, reservation);
        var recordPath = DurableOperationLedger.GetRecordPath(
            fixture.LedgerRoot, request.RepositoryId, request.OperationId);
        var json = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        var guardJournal = json["guardJournal"]!.AsArray();
        switch (tampering)
        {
            case "sequence":
                guardJournal[1]!["sequence"] = 99;
                break;
            case "reservation":
                guardJournal[1]!["reservationToken"] = Guid.NewGuid().ToString();
                break;
            case "orphan-intent":
                json["preEffectActivity"]!["activeAdmissions"] = new JsonArray();
                break;
            case "release-without-attempt":
                guardJournal[1]!["kind"] = (int)OperationGuardJournalKind.ConfirmedInactive;
                break;
            case "duplicate-marker":
                guardJournal[1]!["kind"] = (int)OperationGuardJournalKind.ActivityAdmitted;
                break;
            case "untracked-admission":
                json["preEffectActivity"]!["activeAdmissions"]![0]!["token"]
                    = Guid.NewGuid().ToString();
                break;
            case "missing-event":
                guardJournal.Add(null);
                break;
            case "no-admission-history":
                json["preEffectActivity"]!["hasAdmissionHistory"] = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(tampering));
        }
        File.WriteAllText(recordPath, json.ToJsonString());

        fixture.CreatePlainLedger().Load(request.RepositoryId, request.OperationId)
            .Match(_ => throw new ShouldAssertException("Tampered guard history was accepted."),
                error => error.Kind)
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
    }

    [Fact]
    public async Task restart_rejects_an_extra_unresolved_guard_after_first_effect_intent()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        (await fixture.CreateAuthority(new SimulatedBroker(fixture)).ApplyAsync(request))
            .IsOk.ShouldBeTrue();
        var recordPath = DurableOperationLedger.GetRecordPath(
            fixture.LedgerRoot, request.RepositoryId, request.OperationId);
        var json = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        var guardJournal = json["guardJournal"]!.AsArray();
        var extraToken = Guid.NewGuid().ToString();
        var marker = guardJournal[0]!.DeepClone();
        marker["sequence"] = 3;
        marker["admissionToken"] = extraToken;
        var intent = guardJournal[1]!.DeepClone();
        intent["sequence"] = 4;
        intent["admissionToken"] = extraToken;
        guardJournal.Add(marker);
        guardJournal.Add(intent);
        File.WriteAllText(recordPath, json.ToJsonString());

        fixture.CreatePlainLedger().Load(request.RepositoryId, request.OperationId)
            .Match(_ => throw new ShouldAssertException("Extra unresolved guard was accepted."),
                error => error.Kind)
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
    }

    [Fact]
    public async Task missing_guard_history_cannot_drain_or_extend_an_active_admission()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var recordPath = DurableOperationLedger.GetRecordPath(
            fixture.LedgerRoot, request.RepositoryId, request.OperationId);
        var json = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        json.Remove("guardJournal");
        File.WriteAllText(recordPath, json.ToJsonString());

        var restarted = fixture.CreatePlainLedger();
        restarted.Load(request.RepositoryId, request.OperationId).IsOk.ShouldBeTrue();
        restarted.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsErr.ShouldBeTrue();
        restarted.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).PreEffectActivity.ActiveAdmissions
            .ShouldContain(activity);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task missing_guard_history_cannot_admit_work_after_an_earlier_activity_drained()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        var recordPath = DurableOperationLedger.GetRecordPath(
            fixture.LedgerRoot, request.RepositoryId, request.OperationId);
        var json = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        json.Remove("guardJournal");
        File.WriteAllText(recordPath, json.ToJsonString());

        var restarted = fixture.CreatePlainLedger();
        restarted.Load(request.RepositoryId, request.OperationId).IsOk.ShouldBeTrue();
        restarted.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Theory]
    [InlineData("backup-path")]
    [InlineData("backup-hash")]
    [InlineData("missing-cleanup")]
    [InlineData("wrong-cleanup-kind")]
    public async Task restart_rejects_tampered_backup_or_cleanup_authority(string tampering)
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var admission = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.Ledger.AppendBackupIntent(plan, request.OperationId, 1, admission, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.AppendBackupCreationAdmission(
                plan, request.OperationId, 1, admission, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();

        var recordPath = DurableOperationLedger.GetRecordPath(
            fixture.LedgerRoot, request.RepositoryId, request.OperationId);
        var json = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        switch (tampering)
        {
            case "backup-path":
                json["backupJournal"]!.AsArray()[0]!["artifactPath"] = fixture.TargetPath;
                break;
            case "backup-hash":
                json["backupJournal"]!.AsArray()[0]!["expectedHash"] = new string('c', 64);
                break;
            case "missing-cleanup":
                json.Remove("cleanupJournal");
                break;
            case "wrong-cleanup-kind":
                json["cleanupJournal"]!.AsArray()[0]!["artifacts"]!.AsArray()[0]!["kind"] =
                    (int)OperationCleanupArtifactKind.Staging;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(tampering));
        }
        File.WriteAllText(recordPath, json.ToJsonString());

        fixture.CreatePlainLedger().Load(request.RepositoryId, request.OperationId)
            .Match(_ => throw new ShouldAssertException("Tampered backup authority was accepted."),
                error => error.Kind)
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
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
    public async Task first_effect_handoff_requires_every_planned_artifact_to_be_verified()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.JournalGuardIntent(request, activity, reservation);

        var result = fixture.Ledger.BeginApplyingWithFirstEffectIntent(
            plan, request.OperationId, activity, reservation,
            OperationGuardTokenSet.Expected(plan.Envelope.Payload));

        result.IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
    }

    [Fact]
    public async Task first_effect_handoff_refuses_guard_tokens_without_a_durable_acquisition_intent()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.JournalVerifiedArtifacts(plan, request, activity, reservation);

        var handoff = fixture.Ledger.BeginApplyingWithFirstEffectIntent(
            plan, request.OperationId, activity, reservation,
            OperationGuardTokenSet.Expected(plan.Envelope.Payload));

        handoff.IsErr.ShouldBeTrue();
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        operation.PreEffectActivity.ActiveAdmissions.ShouldContain(activity);
        operation.EffectJournal.ShouldBeEmpty();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task a_guard_attempt_cannot_drain_its_activity_before_inactivity_is_recorded()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.JournalGuardIntent(request, activity, reservation);

        var drained = fixture.Ledger.CompletePreEffectActivity(
            request.RepositoryId, request.OperationId, activity, reservation,
            OperationLifecycleState.RejectedUnsafeCoordination);

        drained.IsErr.ShouldBeTrue();
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        operation.PreEffectActivity.ActiveAdmissions.ShouldContain(activity);
        operation.CleanupJournal.ShouldBeEmpty();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task guard_journal_refuses_out_of_order_and_stale_reservation_transitions()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var first = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, first)
            .Match(value => value, error => throw new IOException(error.Message));

        fixture.Ledger.AppendGuardConfirmedInactive(
                request.RepositoryId, request.OperationId, activity, first)
            .IsErr.ShouldBeTrue();
        fixture.JournalGuardIntent(request, activity, first);
        fixture.Ledger.AppendGuardAcquisitionIntent(
                request.RepositoryId, request.OperationId, activity, first)
            .IsErr.ShouldBeTrue();
        first.Dispose();
        using var successor = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        fixture.Ledger.AppendGuardConfirmedInactive(
                request.RepositoryId, request.OperationId, activity, successor)
            .IsErr.ShouldBeTrue();

        var operation = fixture.LoadOperation(request);
        operation.GuardJournal.Select(entry => entry.Kind).ShouldBe([
            OperationGuardJournalKind.ActivityAdmitted,
            OperationGuardJournalKind.AcquisitionIntent
        ]);
        operation.PreEffectActivity.ActiveAdmissions.ShouldContain(activity);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task cancelled_guard_activity_records_inactivity_before_it_can_drain()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.JournalGuardIntent(request, activity, reservation);
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsOk.ShouldBeTrue();

        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsErr.ShouldBeTrue();
        fixture.Ledger.AppendGuardConfirmedInactive(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();

        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        operation.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        operation.GuardJournal[^1].Kind.ShouldBe(OperationGuardJournalKind.ConfirmedInactive);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task guard_journal_timestamps_do_not_move_backward_with_the_clock()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var first = fixture.LoadOperation(request).GuardJournal[^1].CommittedAtUtc;
        fixture.Now = fixture.Now.AddMinutes(1);
        fixture.JournalGuardIntent(request, activity, reservation);
        var intentAt = fixture.LoadOperation(request).GuardJournal[^1].CommittedAtUtc;
        fixture.Now = fixture.Now.AddMinutes(-1);

        fixture.Ledger.AppendGuardConfirmedInactive(
                request.RepositoryId, request.OperationId, activity, reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .IsOk.ShouldBeTrue();

        fixture.LoadOperation(request).GuardJournal
            .Select(entry => entry.CommittedAtUtc)
            .ShouldAllBe(committed => committed >= first);
        fixture.LoadOperation(request).GuardJournal[^1].CommittedAtUtc
            .ShouldBe(intentAt);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task first_effect_handoff_rejects_a_verified_prefix_of_a_multi_effect_plan()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(twoEffects: true);
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.JournalVerifiedArtifacts(plan, request, activity, reservation, 1);
        fixture.JournalGuardIntent(request, activity, reservation);

        fixture.Ledger.BeginApplyingWithFirstEffectIntent(
                plan, request.OperationId, activity, reservation,
                OperationGuardTokenSet.Expected(plan.Envelope.Payload))
            .IsErr.ShouldBeTrue();

        fixture.JournalVerifiedArtifacts(plan, request, activity, reservation, 2);
        fixture.Ledger.BeginApplyingWithFirstEffectIntent(
                plan, request.OperationId, activity, reservation,
                OperationGuardTokenSet.Expected(plan.Envelope.Payload))
            .IsOk.ShouldBeTrue();
        fixture.LoadOperation(request).StagingJournal
            .Where(entry => entry.Kind == OperationStagingJournalKind.Verified)
            .Select(entry => entry.EffectSequence).ShouldBe([1, 2]);
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task first_effect_handoff_rejects_artifacts_verified_under_an_earlier_activity(
        bool replacement)
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: replacement);
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var earlier = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.JournalVerifiedArtifacts(plan, request, earlier, reservation);
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId, request.OperationId, earlier, reservation)
            .IsOk.ShouldBeTrue();
        var current = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.JournalGuardIntent(request, current, reservation);

        fixture.Ledger.BeginApplyingWithFirstEffectIntent(
                plan, request.OperationId, current, reservation,
                OperationGuardTokenSet.Expected(plan.Envelope.Payload))
            .IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        if (replacement)
            File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
        else
            File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task first_effect_handoff_waits_for_other_pre_effect_activity_and_defeats_late_cancellation()
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
        var otherActivity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.JournalVerifiedArtifacts(plan, request, activity, reservation);
        fixture.JournalGuardIntent(request, activity, reservation);

        fixture.Ledger.BeginApplyingWithFirstEffectIntent(
                plan, request.OperationId, activity, reservation,
                OperationGuardTokenSet.Expected(plan.Envelope.Payload))
            .IsErr.ShouldBeTrue();
        fixture.Ledger.CompletePreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                otherActivity,
                reservation)
            .IsOk.ShouldBeTrue();
        fixture.Ledger.BeginApplyingWithFirstEffectIntent(
                plan, request.OperationId, activity, reservation,
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
    public async Task first_effect_handoff_closes_cancellation_with_owner_and_intent_in_one_record()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        fixture.JournalVerifiedArtifacts(plan, request, activity, reservation);
        fixture.JournalGuardIntent(request, activity, reservation);
        var preparedAt = fixture.LoadOperation(request).Transitions[^1].CommittedAtUtc;
        fixture.Now = fixture.Now.AddMinutes(-1);

        var handedOff = fixture.Ledger.BeginApplyingWithFirstEffectIntent(
                plan, request.OperationId, activity, reservation,
                OperationGuardTokenSet.Expected(plan.Envelope.Payload))
            .Match(value => value, error => throw new IOException(error.Message));

        handedOff.CurrentState.ShouldBe(OperationLifecycleState.Applying);
        handedOff.PreEffectActivity.IsClosed.ShouldBeTrue();
        handedOff.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        handedOff.OwnerLease.ShouldNotBeNull();
        handedOff.OwnerLease.FenceEpoch.ShouldBe(reservation.FenceEpoch);
        handedOff.Transitions[^1].CommittedAtUtc.ShouldBeGreaterThanOrEqualTo(preparedAt);
        handedOff.EffectJournal.ShouldHaveSingleItem();
        handedOff.EffectJournal[0].Kind.ShouldBe(OperationEffectJournalKind.Intent);
        handedOff.EffectJournal[0].EffectHash.ShouldBe(
            CanonicalChangePlan.ComputeEffectHash(plan.Envelope.Payload.Effects[0]));
        var reloaded = fixture.LoadOperation(request);
        reloaded.CurrentState.ShouldBe(OperationLifecycleState.Applying);
        reloaded.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        reloaded.EffectJournal[0].EvidenceHash.ShouldBe(handedOff.EffectJournal[0].EvidenceHash);
        fixture.Ledger.CancelBeforeFirstEffect(request.RepositoryId, request.OperationId)
            .IsErr.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task first_effect_handoff_rejects_wrong_plan_guard_and_activity_without_changing_state()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        using var reservation = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        var activity = fixture.Ledger.BeginPreEffectActivity(
                request.RepositoryId, request.OperationId, reservation)
            .Match(value => value, error => throw new IOException(error.Message));
        var guards = OperationGuardTokenSet.Expected(plan.Envelope.Payload);
        var wrongPlan = plan with { Key = plan.Key with { PlanHash = new string('b', 64) } };
        var wrongGuards = guards.SetItem(0, guards[0] with { VersionToken = "wrong" });
        var wrongActivity = activity with { Token = Guid.NewGuid() };
        fixture.JournalGuardIntent(request, activity, reservation);

        fixture.Ledger.BeginApplyingWithFirstEffectIntent(
                wrongPlan, request.OperationId, activity, reservation, guards)
            .IsErr.ShouldBeTrue();
        fixture.Ledger.BeginApplyingWithFirstEffectIntent(
                plan, request.OperationId, activity, reservation, wrongGuards)
            .IsErr.ShouldBeTrue();
        fixture.Ledger.BeginApplyingWithFirstEffectIntent(
                plan, request.OperationId, wrongActivity, reservation, guards)
            .IsErr.ShouldBeTrue();

        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        operation.PreEffectActivity.ActiveAdmissions.ShouldContain(activity);
        operation.EffectJournal.ShouldBeEmpty();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task apply_verifies_staging_and_backup_before_acquiring_the_effect_guard()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var broker = new SimulatedBroker(fixture);

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsOk.ShouldBeTrue();
        broker.ArtifactsVerifiedAtAcquire.ShouldBeTrue();
        var operation = fixture.LoadOperation(request);
        operation.StagingJournal[^1].Kind.ShouldBe(OperationStagingJournalKind.Verified);
        operation.BackupJournal[^1].Kind.ShouldBe(OperationBackupJournalKind.Verified);
    }

    [Fact]
    public async Task apply_verifies_every_effect_artifact_before_broker_acquisition()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(twoEffects: true, replacement: true);
        var broker = new SimulatedBroker(fixture) { DeclineAfterInspect = true };

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => throw new InvalidOperationException("Apply unexpectedly succeeded."),
            error => error.Kind).ShouldBe(JournaledApplyErrorKind.UnsafeCoordination);
        broker.Acquired.ShouldBeTrue();
        broker.ArtifactsVerifiedAtAcquire.ShouldBeTrue();
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.RejectedUnsafeCoordination);
        operation.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        operation.CleanupJournal[0].Kind.ShouldBe(OperationCleanupJournalKind.InventoryPending);
        operation.StagingJournal.Where(entry => entry.Kind == OperationStagingJournalKind.Verified)
            .Select(entry => entry.EffectSequence).ShouldBe([1, 2]);
        operation.BackupJournal.Where(entry => entry.Kind == OperationBackupJournalKind.Verified)
            .Select(entry => entry.EffectSequence).ShouldBe([1]);
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
        File.Exists(Path.Combine(fixture.RepositoryRoot, "src", "Added2.cs")).ShouldBeFalse();
    }

    [Fact]
    public async Task guard_rejection_cleans_every_verified_artifact_before_releasing_the_plan_pin()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(twoEffects: true, replacement: true);
        var broker = new SimulatedBroker(fixture) { DeclineAfterInspect = true };
        var plan = fixture.StoredPlan!;
        var cleanup = new JournaledNoEffectCleanup(
            fixture.Ledger,
            fixture.Store,
            new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
            new LinuxBackupCleanupBoundary(plan.Envelope.Payload.Coordination.BackupRoot));

        var result = await fixture.CreateAuthority(broker, noEffectCleanup: cleanup)
            .ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        broker.ArtifactsVerifiedAtAcquire.ShouldBeTrue();
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.RejectedUnsafeCoordination);
        operation.CleanupJournal[^1].Kind.ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        operation.CleanupJournal[0].Artifacts.Length.ShouldBe(3);
        operation.CleanupJournal[0].Artifacts.All(artifact => !File.Exists(artifact.ArtifactPath))
            .ShouldBeTrue();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
        File.Exists(Path.Combine(fixture.RepositoryRoot, "src", "Added2.cs")).ShouldBeFalse();
    }

    [Fact]
    public async Task guard_rejection_keeps_the_plan_pinned_when_physical_cleanup_cannot_be_verified()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var broker = new SimulatedBroker(fixture)
        {
            DeclineAfterInspect = true,
            TamperArtifactBeforeDecline = true
        };
        var plan = fixture.StoredPlan!;
        var cleanup = new JournaledNoEffectCleanup(
            fixture.Ledger,
            fixture.Store,
            new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
            new LinuxBackupCleanupBoundary(plan.Envelope.Payload.Coordination.BackupRoot));

        var result = await fixture.CreateAuthority(broker, noEffectCleanup: cleanup)
            .ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => throw new InvalidOperationException("Apply unexpectedly succeeded."),
            error => error.Kind).ShouldBe(JournaledApplyErrorKind.CleanupRequired);
        broker.ArtifactsVerifiedAtAcquire.ShouldBeTrue();
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.RejectedUnsafeCoordination);
        operation.CleanupJournal[^1].Kind.ShouldBe(OperationCleanupJournalKind.InventoryPending);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task failed_preflight_and_cleanup_retain_both_failure_causes(
        bool cancellationWins)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture)
        {
            FailPreflight = true,
            CancelDuringPreflight = cancellationWins,
            TamperArtifactBeforeFailedPreflight = true
        };
        var plan = fixture.StoredPlan!;
        var cleanup = new JournaledNoEffectCleanup(
            fixture.Ledger, fixture.Store,
            new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot));

        var result = await fixture.CreateAuthority(broker, noEffectCleanup: cleanup)
            .ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        var error = result.Match(_ => throw new InvalidOperationException("Apply unexpectedly succeeded."),
            value => value);
        error.Kind.ShouldBe(JournaledApplyErrorKind.CleanupRequired);
        var causes = error.Cause.ShouldBeOfType<AggregateException>().InnerExceptions;
        causes.Count.ShouldBe(2);
        causes[0].Message.ShouldContain("preflight");
        causes[1].Message.ShouldContain("artifact");
        fixture.LoadOperation(request).CurrentState.ShouldBe(cancellationWins
            ? OperationLifecycleState.CancelledNoEffect
            : OperationLifecycleState.RejectedUnsafeCoordination);
        fixture.LoadOperation(request).CleanupJournal[^1].Kind
            .ShouldBe(OperationCleanupJournalKind.InventoryPending);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task guard_rejection_keeps_the_plan_pinned_when_its_terminal_record_cannot_be_written()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture) { DeclineAfterInspect = true };
        var plan = fixture.StoredPlan!;
        var cleanup = new JournaledNoEffectCleanup(
            fixture.Ledger, fixture.Store,
            new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot));

        var result = await fixture.CreateAuthority(
                broker, ledgerDurability: new FailNthReplace(7), noEffectCleanup: cleanup)
            .ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => throw new InvalidOperationException("Apply unexpectedly succeeded."),
            error => error.Kind).ShouldBe(JournaledApplyErrorKind.LedgerUnavailable);
        broker.Acquired.ShouldBeTrue();
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        operation.PreEffectActivity.ActiveAdmissions.ShouldHaveSingleItem();
        operation.CleanupJournal.ShouldBeEmpty();
        var artifact = operation.StagingJournal.Single(entry =>
            entry.Kind == OperationStagingJournalKind.Verified);
        File.Exists(artifact.ArtifactPath).ShouldBeTrue();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task cancellation_and_guard_rejection_have_one_atomic_no_effect_winner()
    {
        using var cancellationFirst = new ApplyFixture();
        var cancelledRequest = cancellationFirst.Prepare();
        var cancelledPlan = cancellationFirst.StoredPlan!;
        using (var reservation = await cancellationFirst.Reservations.AcquireAsync(
                   cancelledPlan.Envelope.Payload.Repository, CancellationToken.None))
        {
            var activity = cancellationFirst.Ledger.BeginPreEffectActivity(
                    cancelledRequest.RepositoryId, cancelledRequest.OperationId, reservation)
                .Match(value => value, error => throw new IOException(error.Message));
            cancellationFirst.JournalVerifiedArtifacts(
                cancelledPlan, cancelledRequest, activity, reservation);
            cancellationFirst.Ledger.CancelBeforeFirstEffect(
                    cancelledRequest.RepositoryId, cancelledRequest.OperationId)
                .IsOk.ShouldBeTrue();

            var resolved = cancellationFirst.Ledger.CompletePreEffectActivity(
                cancelledRequest.RepositoryId, cancelledRequest.OperationId,
                activity, reservation, OperationLifecycleState.RejectedUnsafeCoordination);

            resolved.IsOk.ShouldBeTrue();
            resolved.Match(value => value, _ => null!).CurrentState
                .ShouldBe(OperationLifecycleState.CancelledNoEffect);
            resolved.Match(value => value, _ => null!).PreEffectActivity.ActiveAdmissions
                .ShouldBeEmpty();
            resolved.Match(value => value, _ => null!).CleanupJournal[0].Artifacts
                .ShouldHaveSingleItem();
        }

        using var rejectionFirst = new ApplyFixture();
        var rejectedRequest = rejectionFirst.Prepare();
        var rejectedPlan = rejectionFirst.StoredPlan!;
        using var rejectedReservation = await rejectionFirst.Reservations.AcquireAsync(
            rejectedPlan.Envelope.Payload.Repository, CancellationToken.None);
        var rejectedActivity = rejectionFirst.Ledger.BeginPreEffectActivity(
                rejectedRequest.RepositoryId, rejectedRequest.OperationId, rejectedReservation)
            .Match(value => value, error => throw new IOException(error.Message));
        rejectionFirst.JournalVerifiedArtifacts(
            rejectedPlan, rejectedRequest, rejectedActivity, rejectedReservation);

        var rejection = rejectionFirst.Ledger.CompletePreEffectActivity(
            rejectedRequest.RepositoryId, rejectedRequest.OperationId,
            rejectedActivity, rejectedReservation,
            OperationLifecycleState.RejectedUnsafeCoordination);

        rejection.IsOk.ShouldBeTrue();
        rejection.Match(value => value, _ => null!).CurrentState
            .ShouldBe(OperationLifecycleState.RejectedUnsafeCoordination);
        rejection.Match(value => value, _ => null!).PreEffectActivity.ActiveAdmissions
            .ShouldBeEmpty();
        rejection.Match(value => value, _ => null!).CleanupJournal[0].Artifacts
            .ShouldHaveSingleItem();
        rejectionFirst.Ledger.CancelBeforeFirstEffect(
                rejectedRequest.RepositoryId, rejectedRequest.OperationId)
            .IsErr.ShouldBeTrue();
    }

    [Fact]
    public async Task interrupted_staging_keeps_its_admission_and_never_acquires_an_effect_guard()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture);
        var stagingRoot = fixture.StoredPlan!.Envelope.Payload.Coordination.StagingRoot;
        var staging = new ThrowAfterArtifactBoundary(
            new LinuxStagingArtifactBoundary(stagingRoot), fixture, request);

        var result = await fixture.CreateAuthority(
            broker, stagingBoundary: staging).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        broker.Acquired.ShouldBeFalse();
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        operation.StagingJournal[^1].Kind.ShouldBe(OperationStagingJournalKind.CreationAdmitted);
        operation.PreEffectActivity.ActiveAdmissions.ShouldHaveSingleItem();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task cancellation_during_staging_does_not_acquire_an_effect_guard()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture);
        var staging = new CancelAfterArtifactBoundary(
            new LinuxStagingArtifactBoundary(
                fixture.StoredPlan!.Envelope.Payload.Coordination.StagingRoot),
            fixture, request);

        var result = await fixture.CreateAuthority(
            broker, stagingBoundary: staging).ApplyAsync(request);

        result.IsOk.ShouldBeTrue();
        broker.Acquired.ShouldBeFalse();
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        operation.StagingJournal[^1].Kind.ShouldBe(OperationStagingJournalKind.Verified);
        operation.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task cancellation_after_staging_cleans_the_artifact_and_releases_the_plan_pin()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var broker = new SimulatedBroker(fixture);
        var staging = new CancelAfterArtifactBoundary(
            new LinuxStagingArtifactBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
            fixture, request);
        var cleanup = new JournaledNoEffectCleanup(
            fixture.Ledger, fixture.Store,
            new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
            new LinuxBackupCleanupBoundary(plan.Envelope.Payload.Coordination.BackupRoot));

        var result = await fixture.CreateAuthority(
                broker, stagingBoundary: staging, noEffectCleanup: cleanup)
            .ApplyAsync(request);

        result.IsOk.ShouldBeTrue();
        broker.Acquired.ShouldBeFalse();
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        operation.CleanupJournal[^1].Kind.ShouldBe(OperationCleanupJournalKind.CleanupVerified);
        operation.CleanupJournal[0].Artifacts.ShouldHaveSingleItem();
        File.Exists(operation.CleanupJournal[0].Artifacts[0].ArtifactPath).ShouldBeFalse();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeFalse();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task cancellation_keeps_the_plan_pinned_when_physical_cleanup_cannot_be_verified()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var broker = new SimulatedBroker(fixture);
        var staging = new CancelAfterArtifactBoundary(
            new LinuxStagingArtifactBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
            fixture, request, tamperAfterCancellation: true);
        var cleanup = new JournaledNoEffectCleanup(
            fixture.Ledger, fixture.Store,
            new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
            new LinuxBackupCleanupBoundary(plan.Envelope.Payload.Coordination.BackupRoot));

        var result = await fixture.CreateAuthority(
                broker, stagingBoundary: staging, noEffectCleanup: cleanup)
            .ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => throw new InvalidOperationException("Apply unexpectedly succeeded."),
            error => error.Kind).ShouldBe(JournaledApplyErrorKind.CleanupRequired);
        broker.Acquired.ShouldBeFalse();
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        operation.CleanupJournal[^1].Kind.ShouldBe(OperationCleanupJournalKind.InventoryPending);
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task interrupted_backup_keeps_its_admission_and_never_acquires_an_effect_guard()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var broker = new SimulatedBroker(fixture);
        var backupRoot = fixture.StoredPlan!.Envelope.Payload.Coordination.BackupRoot;

        var result = await fixture.CreateAuthority(
            broker,
            backupBoundary: new LinuxBackupArtifactBoundary(
                backupRoot, new RecordingStagingDurability(failedFlush: 1)))
            .ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        broker.Acquired.ShouldBeFalse();
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        operation.StagingJournal[^1].Kind.ShouldBe(OperationStagingJournalKind.Verified);
        operation.BackupJournal[^1].Kind.ShouldBe(OperationBackupJournalKind.CreationAdmitted);
        operation.PreEffectActivity.ActiveAdmissions.ShouldHaveSingleItem();
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
    }

    [Fact]
    public async Task missing_artifact_authority_rejects_before_pre_effect_activity_or_broker_acquisition()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture);
        var apply = new JournaledApplyAuthority(
            fixture.Store, fixture.Ledger, fixture.Reservations, broker);

        var result = await apply.ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.UnsafeCoordination);
        broker.Acquired.ShouldBeFalse();
        fixture.LoadOperation(request).PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task unsafe_artifact_root_rejects_before_intent_or_effect_guard(
        bool backupRoot)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: backupRoot);
        var broker = new SimulatedBroker(fixture);
        var wrongRoot = Path.Combine(fixture.RepositoryRoot, "not-a-control-root");

        var result = await fixture.CreateAuthority(
                broker,
                stagingBoundary: backupRoot ? null : new LinuxStagingArtifactBoundary(wrongRoot),
                backupBoundary: backupRoot ? new LinuxBackupArtifactBoundary(wrongRoot) : null)
            .ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.UnsafeCoordination);
        broker.Acquired.ShouldBeFalse();
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        operation.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        if (backupRoot)
        {
            operation.StagingJournal[^1].Kind.ShouldBe(OperationStagingJournalKind.Verified);
            operation.BackupJournal.ShouldBeEmpty();
            File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
        }
        else
        {
            operation.StagingJournal.ShouldBeEmpty();
            File.Exists(fixture.TargetPath).ShouldBeFalse();
        }
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
    public async Task cancelled_preflight_releases_its_guard_before_the_activity_drains()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture)
        {
            CancelDuringPreflight = true,
            RequireActiveAtDisposeAfterCancellation = true
        };

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsOk.ShouldBeTrue();
        broker.SawActiveAtDispose.ShouldBeTrue();
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        operation.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        operation.EffectJournal.ShouldBeEmpty();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task failed_guard_release_keeps_cancelled_activity_active_and_blocks_cleanup()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture)
        {
            CancelDuringPreflight = true,
            RequireActiveAtDisposeAfterCancellation = true,
            FailDisposeAfterCancellation = true
        };

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.UnsafeCoordination);
        broker.SawActiveAtDispose.ShouldBeTrue();
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
        operation.PreEffectActivity.ActiveAdmissions.ShouldHaveSingleItem();
        fixture.Ledger.ConfirmDurableCancelledNoEffect(
                request.RepositoryId, request.OperationId)
            .IsErr.ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task failed_preflight_releases_guard_before_draining_its_activity()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture)
        {
            FailPreflight = true,
            RequireActiveAtDisposeAfterPreflightFailure = true
        };

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.UnsafeCoordination);
        broker.SawActiveAtDispose.ShouldBeTrue();
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.RejectedUnsafeCoordination);
        operation.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        operation.CleanupJournal[0].Kind.ShouldBe(OperationCleanupJournalKind.InventoryPending);
        operation.EffectJournal.ShouldBeEmpty();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task failed_preflight_and_guard_release_report_both_failures()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture)
        {
            FailPreflight = true,
            RequireActiveAtDisposeAfterPreflightFailure = true,
            FailDisposeAfterPreflightFailure = true
        };

        var result = await fixture.CreateAuthority(broker).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        var error = result.Match(_ => throw new InvalidOperationException("Apply unexpectedly succeeded."),
            value => value);
        error.Kind.ShouldBe(JournaledApplyErrorKind.UnsafeCoordination);
        var causes = error.Cause.ShouldBeOfType<AggregateException>().InnerExceptions;
        causes.Count.ShouldBe(2);
        causes[0].Message.ShouldContain("preflight");
        causes[1].Message.ShouldContain("guard-release");
        broker.SawActiveAtDispose.ShouldBeTrue();
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        operation.PreEffectActivity.ActiveAdmissions.ShouldHaveSingleItem();
        operation.EffectJournal.ShouldBeEmpty();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task uncertain_guard_acquisition_keeps_pre_effect_activity_and_artifacts_pinned()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var plan = fixture.StoredPlan!;
        var broker = new SimulatedBroker(fixture) { ThrowDuringAcquire = true };

        var attempt = await fixture.CreateAuthority(broker).ApplyAsync(request);

        attempt.IsErr.ShouldBeTrue();
        var failure = attempt.Match(
            _ => throw new InvalidOperationException("Apply unexpectedly succeeded."),
            error => error);
        failure.Kind.ShouldBe(JournaledApplyErrorKind.UnsafeCoordination);
        failure.Cause.ShouldBeOfType<IOException>().Message
            .ShouldContain("uncertain guard acquisition");
        broker.Acquired.ShouldBeTrue();
        var unresolved = fixture.LoadOperation(request);
        unresolved.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        unresolved.PreEffectActivity.ActiveAdmissions.ShouldHaveSingleItem();
        unresolved.EffectJournal.ShouldBeEmpty();
        unresolved.CleanupJournal.ShouldBeEmpty();
        unresolved.StagingJournal.Any(entry =>
            entry.Kind == OperationStagingJournalKind.Verified
            && File.Exists(entry.ArtifactPath)).ShouldBeTrue();
        unresolved.BackupJournal.Any(entry =>
            entry.Kind == OperationBackupJournalKind.Verified
            && File.Exists(entry.ArtifactPath)).ShouldBeTrue();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);

        var retryBroker = new SimulatedBroker(fixture);
        var retry = await fixture.CreateAuthority(retryBroker).ApplyAsync(request);
        retry.IsErr.ShouldBeTrue();
        retry.Match(_ => throw new InvalidOperationException("Unsafe retry unexpectedly succeeded."),
            error => error.Kind).ShouldBe(JournaledApplyErrorKind.RecoveryRequired);
        retryBroker.Acquired.ShouldBeFalse();

        using var successor = await fixture.Reservations.AcquireAsync(
            plan.Envelope.Payload.Repository, CancellationToken.None);
        new JournaledNoEffectCleanup(
                fixture.CreatePlainLedger(), fixture.Store,
                new LinuxStagingCleanupBoundary(plan.Envelope.Payload.Coordination.StagingRoot),
                new LinuxBackupCleanupBoundary(plan.Envelope.Payload.Coordination.BackupRoot))
            .Cleanup(request.RepositoryId, request.OperationId, successor)
            .IsErr.ShouldBeTrue();
        fixture.Ledger.HasUnresolvedRepositoryMutation(
                request.RepositoryId, new OperationId(42, Guid.NewGuid()))
            .Match(value => value, error => throw new IOException(error.Message))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task broker_sees_durable_guard_intent_before_acquisition()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture)
        {
            DeclineAfterInspect = true,
            RequireDurableGuardIntentAtAcquire = true
        };

        var attempt = await fixture.CreateAuthority(broker).ApplyAsync(request);

        broker.SawDurableGuardIntent.ShouldBeTrue();
        attempt.IsErr.ShouldBeTrue();
        fixture.LoadOperation(request).EffectJournal.ShouldBeEmpty();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task completed_guard_refusal_records_inactivity_before_no_effect_resolution()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();

        var attempt = await fixture.CreateAuthority(
            new SimulatedBroker(fixture) { DeclineAfterInspect = true }).ApplyAsync(request);

        attempt.IsErr.ShouldBeTrue();
        var operation = fixture.LoadOperation(request);
        operation.GuardJournal.Select(entry => entry.Kind).ShouldBe([
            OperationGuardJournalKind.ActivityAdmitted,
            OperationGuardJournalKind.AcquisitionIntent,
            OperationGuardJournalKind.ConfirmedInactive
        ]);
        operation.CurrentState.ShouldBe(OperationLifecycleState.RejectedUnsafeCoordination);
        operation.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        operation.EffectJournal.ShouldBeEmpty();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task failed_guard_intent_record_prevents_broker_acquisition()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var broker = new SimulatedBroker(fixture);

        var attempt = await fixture.CreateAuthority(
            broker,
            new FailGuardJournalReplace(OperationGuardJournalKind.AcquisitionIntent))
            .ApplyAsync(request);

        attempt.IsErr.ShouldBeTrue();
        attempt.Match(_ => throw new InvalidOperationException("Apply unexpectedly succeeded."),
            error => error.Kind).ShouldBe(JournaledApplyErrorKind.LedgerUnavailable);
        broker.Acquired.ShouldBeFalse();
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        operation.GuardJournal[^1].Kind.ShouldBe(OperationGuardJournalKind.ActivityAdmitted);
        operation.PreEffectActivity.ActiveAdmissions.ShouldHaveSingleItem();
        operation.CleanupJournal.ShouldBeEmpty();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
    }

    [Fact]
    public async Task failed_guard_inactivity_record_keeps_no_effect_cleanup_blocked()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = new ApplyFixture();
        var request = fixture.Prepare(replacement: true);
        var broker = new SimulatedBroker(fixture) { DeclineAfterInspect = true };

        var attempt = await fixture.CreateAuthority(
            broker,
            new FailGuardJournalReplace(OperationGuardJournalKind.ConfirmedInactive))
            .ApplyAsync(request);

        attempt.IsErr.ShouldBeTrue();
        attempt.Match(_ => throw new InvalidOperationException("Apply unexpectedly succeeded."),
            error => error.Kind).ShouldBe(JournaledApplyErrorKind.LedgerUnavailable);
        broker.Acquired.ShouldBeTrue();
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        operation.GuardJournal[^1].Kind.ShouldBe(OperationGuardJournalKind.AcquisitionIntent);
        operation.PreEffectActivity.ActiveAdmissions.ShouldHaveSingleItem();
        operation.CleanupJournal.ShouldBeEmpty();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.OriginalContent);
    }

    [Fact]
    public async Task failed_preflight_and_terminal_record_preserve_the_preflight_cause()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture) { FailPreflight = true };

        var result = await fixture.CreateAuthority(
                broker, ledgerDurability: new FailNthReplace(7)).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        var error = result.Match(_ => throw new InvalidOperationException("Apply unexpectedly succeeded."),
            value => value);
        error.Kind.ShouldBe(JournaledApplyErrorKind.LedgerUnavailable);
        error.Message.ShouldContain("durably resolved");
        error.Cause.ShouldBeOfType<IOException>().Message.ShouldContain("preflight");
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        operation.PreEffectActivity.ActiveAdmissions.ShouldHaveSingleItem();
        operation.CleanupJournal.ShouldBeEmpty();
        fixture.ReloadPlan().Pins.Any(pin => pin.Kind == ChangePlanPinKind.OperationLedger)
            .ShouldBeTrue();
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task rejected_first_effect_handoff_releases_guard_without_an_effect()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var broker = new SimulatedBroker(fixture);

        var result = await fixture.CreateAuthority(
            broker, new FailNthReplace(6)).ApplyAsync(request);

        result.IsErr.ShouldBeTrue();
        result.Match(_ => null!, error => error).Kind
            .ShouldBe(JournaledApplyErrorKind.LedgerUnavailable);
        var operation = fixture.LoadOperation(request);
        operation.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        operation.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        operation.EffectJournal.ShouldBeEmpty();
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
    [InlineData(6, true)]
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
        var visible = fixture.LoadOperation(request);
        if (failedFlush == 1)
        {
            visible.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
            visible.EffectJournal.ShouldBeEmpty();
        }
        else
        {
            visible.CurrentState.ShouldBe(OperationLifecycleState.Applying);
            visible.EffectJournal.ShouldHaveSingleItem();
            visible.EffectJournal[0].Kind.ShouldBe(OperationEffectJournalKind.Intent);
            visible.PreEffectActivity.IsClosed.ShouldBeTrue();
            visible.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        }
        File.Exists(fixture.TargetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task uncertain_first_effect_handoff_durability_blocks_later_mutation()
    {
        using var fixture = new ApplyFixture();
        var request = fixture.Prepare();
        var failingDurability = new FailDirectoryFlushFrom(6);

        (await fixture.CreateAuthority(
                new SimulatedBroker(fixture), failingDurability).ApplyAsync(request))
            .IsErr.ShouldBeTrue();
        var visible = fixture.LoadOperation(request);
        visible.CurrentState.ShouldBe(OperationLifecycleState.Applying);
        visible.EffectJournal.ShouldHaveSingleItem();
        visible.EffectJournal[0].Kind.ShouldBe(OperationEffectJournalKind.Intent);
        visible.PreEffectActivity.IsClosed.ShouldBeTrue();
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
        fixture.LoadOperation(request).CurrentState
            .ShouldBe(OperationLifecycleState.RejectedUnsafeCoordination);
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
            new FailNthDirectoryFlush(9)).ApplyAsync(request);

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
            new FailNthDirectoryFlush(8)).ApplyAsync(request);

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
            new FailNthReplace(7)).ApplyAsync(request);

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
            new FailNthDirectoryFlush(11)).ApplyAsync(request);

        result.IsOk.ShouldBeTrue();
        fixture.LoadOperation(request).CurrentState.ShouldBe(OperationLifecycleState.Applied);
        File.ReadAllBytes(fixture.TargetPath).ShouldBe(fixture.Content);
    }

    [Fact]
    public async Task persistent_terminal_flush_failure_blocks_later_mutation()
    {
        using var fixture = new ApplyFixture();
        var firstRequest = fixture.Prepare();
        var durability = new FailDirectoryFlushFrom(11);

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
        internal bool RequireActiveAtDisposeAfterCancellation { get; init; }
        internal bool FailDisposeAfterCancellation { get; init; }
        internal bool FailDisposeAfterPreflightFailure { get; init; }
        internal bool FailPreflight { get; init; }
        internal bool ThrowDuringAcquire { get; init; }
        internal bool RequireDurableGuardIntentAtAcquire { get; init; }
        internal bool TamperArtifactBeforeFailedPreflight { get; init; }
        internal bool DeclineAfterInspect { get; init; }
        internal bool TamperArtifactBeforeDecline { get; init; }
        internal bool RequireActiveAtDisposeAfterPreflightFailure { get; init; }
        internal bool SawActiveAtDispose { get; private set; }
        internal bool Acquired { get; private set; }
        internal bool SawDurableGuardIntent { get; private set; }
        internal bool SawDurableIntent { get; private set; }
        internal bool SawDurableAdmission { get; private set; }
        internal bool ReservationWasHeldThroughVerification { get; private set; }
        internal bool ArtifactsVerifiedAtAcquire { get; private set; }

        public Task<IJournaledRepositoryEffectSession?> AcquireAsync(
            StoredChangePlan plan,
            PreEffectActivityAdmission activity,
            RepositoryMutationReservation reservation,
            CancellationToken cancellationToken)
        {
            reservation.AssertHeld(plan.Key.RepositoryId);
            var operation = fixture.LoadOperation(fixture.Request!);
            if (operation.CurrentState != OperationLifecycleState.Prepared
                || !operation.PreEffectActivity.ActiveAdmissions.Contains(activity))
                return Task.FromResult<IJournaledRepositoryEffectSession?>(null);
            ArtifactsVerifiedAtAcquire = plan.Envelope.Payload.Effects.All(effect =>
                (!effect.HasReplacementContent || operation.StagingJournal.Any(entry =>
                    entry.EffectSequence == effect.Sequence
                    && entry.Kind == OperationStagingJournalKind.Verified
                    && File.Exists(entry.ArtifactPath)
                    && File.ReadAllBytes(entry.ArtifactPath).SequenceEqual(effect.ReplacementContent)))
                && (!effect.HasOriginalContent || operation.BackupJournal.Any(entry =>
                    entry.EffectSequence == effect.Sequence
                    && entry.Kind == OperationBackupJournalKind.Verified
                    && File.Exists(entry.ArtifactPath)
                    && File.ReadAllBytes(entry.ArtifactPath).SequenceEqual(effect.OriginalContent))));
            if (RequireDurableGuardIntentAtAcquire)
            {
                var recordPath = DurableOperationLedger.GetRecordPath(
                    fixture.LedgerRoot, fixture.Request!.RepositoryId,
                    fixture.Request.OperationId);
                var guardJournal = JsonNode.Parse(File.ReadAllText(recordPath))?
                    ["guardJournal"]?.AsArray();
                SawDurableGuardIntent = guardJournal is { Count: 2 }
                                        && guardJournal[^1]?["kind"]?.GetValue<int>() == 1
                                        && guardJournal[^1]?["admissionToken"]?.GetValue<string>()
                                        == activity.Token.ToString();
            }
            Acquired = true;
            if (ThrowDuringAcquire)
            {
                return Task.FromException<IJournaledRepositoryEffectSession?>(
                    new IOException("Injected uncertain guard acquisition."));
            }
            if (DeclineAfterInspect)
            {
                if (TamperArtifactBeforeDecline)
                {
                    var staged = operation.StagingJournal.Single(entry =>
                        entry.Kind == OperationStagingJournalKind.Verified);
                    File.WriteAllBytes(staged.ArtifactPath, "tampered"u8.ToArray());
                }
                return Task.FromResult<IJournaledRepositoryEffectSession?>(null);
            }
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
                if (broker.FailPreflight)
                {
                    if (broker.TamperArtifactBeforeFailedPreflight)
                    {
                        var operation = fixture.LoadOperation(fixture.Request!);
                        var staged = operation.StagingJournal.Single(entry =>
                            entry.Kind == OperationStagingJournalKind.Verified);
                        File.WriteAllBytes(staged.ArtifactPath, "tampered"u8.ToArray());
                    }
                    throw new IOException("Injected guard preflight failure.");
                }
                var expectedTarget = plan.Envelope.Payload.Effects[0].HasOriginalContent
                    ? File.Exists(fixture.TargetPath)
                      && File.ReadAllBytes(fixture.TargetPath).SequenceEqual(fixture.OriginalContent)
                    : !File.Exists(fixture.TargetPath);
                return Task.FromResult<GuardedPreflightEvidence?>(
                    !expectedTarget
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

            public ValueTask DisposeAsync()
            {
                if (broker.RequireActiveAtDisposeAfterCancellation)
                {
                    var operation = fixture.LoadOperation(fixture.Request!);
                    operation.CurrentState.ShouldBe(OperationLifecycleState.CancelledNoEffect);
                    operation.PreEffectActivity.ActiveAdmissions.ShouldHaveSingleItem();
                    fixture.Ledger.ConfirmDurableCancelledNoEffect(
                            fixture.Request!.RepositoryId,
                            fixture.Request.OperationId)
                        .IsErr.ShouldBeTrue();
                    broker.SawActiveAtDispose = true;
                    if (broker.FailDisposeAfterCancellation)
                        throw new IOException("Injected guard-release failure.");
                }
                if (broker.RequireActiveAtDisposeAfterPreflightFailure)
                {
                    var operation = fixture.LoadOperation(fixture.Request!);
                    operation.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
                    operation.PreEffectActivity.ActiveAdmissions.ShouldHaveSingleItem();
                    broker.SawActiveAtDispose = true;
                    if (broker.FailDisposeAfterPreflightFailure)
                        throw new IOException("Injected guard-release failure.");
                }
                return ValueTask.CompletedTask;
            }
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

    // The apply-state tests run on every OS. Linux's handle-relative artifact
    // boundary has separate physical tests; this stand-in exercises only the
    // journal handoff on platforms that cannot acquire that boundary.
    private sealed class SimulatedArtifactBoundary
        : IStagingArtifactBoundary, IBackupArtifactBoundary
    {
        IStagingArtifactSession IStagingArtifactBoundary.Acquire(StoredChangePlan plan) => new Session();

        IBackupArtifactSession IBackupArtifactBoundary.Acquire(StoredChangePlan plan) => new Session();

        private sealed class Session : IStagingArtifactSession, IBackupArtifactSession
        {
            public Task<string> CreateAndHashAsync(
                OperationStagingJournalEvent creation,
                ReadOnlyMemory<byte> replacementBytes,
                bool allowExistingAdmittedArtifact) =>
                Task.FromResult(Hash(replacementBytes.ToArray()));

            public Task<string> VerifyExistingAsync(
                OperationStagingJournalEvent verification,
                ReadOnlyMemory<byte> replacementBytes) =>
                Task.FromResult(Hash(replacementBytes.ToArray()));

            public Task<string> CreateAndHashAsync(
                OperationBackupJournalEvent creation,
                ReadOnlyMemory<byte> originalBytes,
                bool allowExistingAdmittedArtifact) =>
                Task.FromResult(Hash(originalBytes.ToArray()));

            public Task<string> VerifyExistingAsync(
                OperationBackupJournalEvent verification,
                ReadOnlyMemory<byte> originalBytes) =>
                Task.FromResult(Hash(originalBytes.ToArray()));

            public void Dispose()
            {
            }
        }
    }

    private sealed class PausingStagingBoundary(IStagingArtifactBoundary inner)
        : IStagingArtifactBoundary
    {
        private readonly TaskCompletionSource _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _resume = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Entered => _entered.Task;

        internal void Resume() => _resume.TrySetResult();

        public IStagingArtifactSession Acquire(StoredChangePlan plan) =>
            new Session(inner.Acquire(plan), _entered, _resume);

        private sealed class Session(
            IStagingArtifactSession inner,
            TaskCompletionSource entered,
            TaskCompletionSource resume) : IStagingArtifactSession
        {
            public async Task<string> CreateAndHashAsync(
                OperationStagingJournalEvent creation,
                ReadOnlyMemory<byte> replacementBytes,
                bool allowExistingAdmittedArtifact)
            {
                entered.TrySetResult();
                await resume.Task;
                return await inner.CreateAndHashAsync(
                    creation, replacementBytes, allowExistingAdmittedArtifact);
            }

            public Task<string> VerifyExistingAsync(
                OperationStagingJournalEvent verification,
                ReadOnlyMemory<byte> replacementBytes) =>
                inner.VerifyExistingAsync(verification, replacementBytes);

            public void Dispose() => inner.Dispose();
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

    private sealed class CancelAfterArtifactBoundary(
        IStagingArtifactBoundary inner,
        ApplyFixture fixture,
        CanonicalApplyRequest request,
        bool tamperAfterCancellation = false) : IStagingArtifactBoundary
    {
        public IStagingArtifactSession Acquire(StoredChangePlan plan) =>
            new Session(inner.Acquire(plan), fixture, request, tamperAfterCancellation);

        private sealed class Session(
            IStagingArtifactSession inner,
            ApplyFixture fixture,
            CanonicalApplyRequest request,
            bool tamperAfterCancellation) : IStagingArtifactSession
        {
            public async Task<string> CreateAndHashAsync(
                OperationStagingJournalEvent creation,
                ReadOnlyMemory<byte> replacementBytes,
                bool allowExistingAdmittedArtifact)
            {
                var hash = await inner.CreateAndHashAsync(
                    creation, replacementBytes, allowExistingAdmittedArtifact);
                fixture.Ledger.CancelBeforeFirstEffect(
                        request.RepositoryId, request.OperationId)
                    .IsOk.ShouldBeTrue();
                if (tamperAfterCancellation)
                    File.WriteAllBytes(creation.ArtifactPath, "tampered"u8.ToArray());
                return hash;
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

    private static async Task<(
        CanonicalApplyRequest Request,
        StoredChangePlan Plan,
        OperationCleanupArtifact Artifact)> PrepareRejectedStagingAsync(ApplyFixture fixture)
    {
        var request = fixture.Prepare();
        var plan = fixture.StoredPlan!;
        var attempt = await fixture.CreateAuthority(
            new SimulatedBroker(fixture) { DeclineAfterInspect = true }).ApplyAsync(request);
        attempt.IsErr.ShouldBeTrue();
        var terminal = fixture.LoadOperation(request);
        terminal.CurrentState.ShouldBe(OperationLifecycleState.RejectedUnsafeCoordination);
        terminal.PreEffectActivity.ActiveAdmissions.ShouldBeEmpty();
        terminal.EffectJournal.ShouldBeEmpty();
        var artifact = terminal.CleanupJournal[0].Artifacts.ShouldHaveSingleItem();
        File.Exists(artifact.ArtifactPath).ShouldBeTrue();
        return (request, plan, artifact);
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
        internal byte[] OriginalContent { get; } = Encoding.UTF8.GetBytes("class Original;\n");
        internal CanonicalApplyRequest? Request { get; private set; }
        internal StoredChangePlan? StoredPlan { get; private set; }
        internal DurableOperationLedger Ledger => _ledger;
        internal DurableChangePlanStore Store => _store;
        internal DurableChangePlanStore CreateStore(IChangePlanStoreDurability? durability = null) =>
            new(_authority.PlanStoreRoot, ChangePlanStoreOptions.Default,
                () => Now, _authority, durability);
        internal (
            DurableOperationLedger Ledger,
            DurableChangePlanStore Store,
            RepositoryMutationReservationProvider Reservations) RestartedComponents()
        {
            var authority = new LocalDurableOperationAuthority(_authority.ControlRoot, () => Now);
            return (
                new DurableOperationLedger(authority.LedgerRoot, () => Now, authority),
                new DurableChangePlanStore(authority.PlanStoreRoot,
                    ChangePlanStoreOptions.Default, () => Now, authority),
                new RepositoryMutationReservationProvider(authority.MutationReservationRoot,
                    authority));
        }
        internal string LedgerRoot => _authority.LedgerRoot;
        internal string PlanStoreRoot => _authority.PlanStoreRoot;
        internal string ReservationRoot => _authority.MutationReservationRoot;
        internal DurableOperationLedger CreatePlainLedger() =>
            new(_authority.LedgerRoot, () => Now, _authority);
        internal StoredChangePlan ReloadPlan() =>
            _store.Load(StoredPlan!.Key)
                .Match(value => value, error => throw new IOException(error.Message));

        internal void JournalVerifiedArtifacts(
            StoredChangePlan plan,
            CanonicalApplyRequest request,
            PreEffectActivityAdmission activity,
            RepositoryMutationReservation reservation,
            int? onlySequence = null)
        {
            foreach (var effect in plan.Envelope.Payload.Effects.Where(effect =>
                         onlySequence is null || effect.Sequence == onlySequence))
            {
                if (effect.HasReplacementContent)
                {
                    _ledger.AppendStagingIntent(
                            plan, request.OperationId, effect.Sequence, activity, reservation)
                        .Match(value => value, error => throw new IOException(error.Message));
                    var creation = _ledger.AppendStagingCreationAdmission(
                            plan, request.OperationId, effect.Sequence, activity, reservation)
                        .Match(value => value, error => throw new IOException(error.Message));
                    _ledger.AppendStagingVerification(
                            plan, request.OperationId, effect.Sequence,
                            Hash(effect.ReplacementContent.ToArray()),
                            creation.CreationToken!.Value, activity, reservation)
                        .Match(value => value, error => throw new IOException(error.Message));
                }
                if (effect.HasOriginalContent)
                {
                    _ledger.AppendBackupIntent(
                            plan, request.OperationId, effect.Sequence, activity, reservation)
                        .Match(value => value, error => throw new IOException(error.Message));
                    var creation = _ledger.AppendBackupCreationAdmission(
                            plan, request.OperationId, effect.Sequence, activity, reservation)
                        .Match(value => value, error => throw new IOException(error.Message));
                    _ledger.AppendBackupVerification(
                            plan, request.OperationId, effect.Sequence,
                            Hash(effect.OriginalContent.ToArray()),
                            creation.CreationToken!.Value, activity, reservation)
                        .Match(value => value, error => throw new IOException(error.Message));
                }
            }
        }
        internal void JournalGuardIntent(
            CanonicalApplyRequest request,
            PreEffectActivityAdmission activity,
            RepositoryMutationReservation reservation) =>
            _ledger.AppendGuardAcquisitionIntent(
                    request.RepositoryId, request.OperationId, activity, reservation)
                .Match(_ => true, error => throw new IOException(error.Message));
        internal RepositoryMutationReservationProvider Reservations => new(
            _authority.MutationReservationRoot, _authority);
        internal DurableOperationLedger CreateLedger(IOperationLedgerDurability durability) =>
            new(_authority.LedgerRoot, () => Now, _authority, durability);

        internal CanonicalApplyRequest Prepare(
            bool pin = true,
            bool twoEffects = false,
            bool replacement = false,
            bool secondReplacement = false)
        {
            if (replacement)
                File.WriteAllBytes(TargetPath, OriginalContent);
            if (secondReplacement)
                File.WriteAllBytes(Path.Combine(RepositoryRoot, "src", "Added2.cs"),
                    OriginalContent);
            var envelope = CreateEnvelope(twoEffects, replacement, secondReplacement);
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
            IOperationLedgerDurability? ledgerDurability = null,
            IStagingArtifactBoundary? stagingBoundary = null,
            IBackupArtifactBoundary? backupBoundary = null,
            JournaledNoEffectCleanup? noEffectCleanup = null)
        {
            foreach (var root in new[]
                     {
                         Path.Combine(_authority.ControlRoot, "staging"),
                         Path.Combine(_authority.ControlRoot, "backup")
                     })
            {
                Directory.CreateDirectory(root);
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                    File.SetUnixFileMode(root,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            return new JournaledApplyAuthority(
                _store,
                ledgerDurability is null
                ? _ledger
                : new DurableOperationLedger(
                    _authority.LedgerRoot, () => Now, _authority, ledgerDurability),
                new RepositoryMutationReservationProvider(_authority.MutationReservationRoot, _authority),
                broker,
                stagingBoundary ?? (OperatingSystem.IsLinux()
                                   && RuntimeInformation.ProcessArchitecture
                                   == System.Runtime.InteropServices.Architecture.X64
                    ? new LinuxStagingArtifactBoundary(
                        Path.Combine(_authority.ControlRoot, "staging"))
                    : new SimulatedArtifactBoundary()),
                backupBoundary ?? (OperatingSystem.IsLinux()
                                  && RuntimeInformation.ProcessArchitecture
                                  == System.Runtime.InteropServices.Architecture.X64
                    ? new LinuxBackupArtifactBoundary(
                        Path.Combine(_authority.ControlRoot, "backup"))
                    : new SimulatedArtifactBoundary()),
                noEffectCleanup);
        }

        internal DurableOperationRecord LoadOperation(CanonicalApplyRequest request) =>
            new DurableOperationLedger(_authority.LedgerRoot, () => Now, _authority)
                .Load(request.RepositoryId, request.OperationId)
                .Match(value => value, error => throw new IOException(error.Message));

        private ChangePlanEnvelope CreateEnvelope(
            bool twoEffects,
            bool replacement,
            bool secondReplacement)
        {
            var controlRoot = _authority.ControlRoot;
            var sourcePath = "src/Added.cs";
            var after = new ChangePlanPathState(
                true, ChangePlanPathKind.RegularFile, Hash(Content), null, 1, [],
                "utf-8", false, "lf", "0644", false, false, null);
            var before = replacement
                ? new ChangePlanPathState(
                    true, ChangePlanPathKind.RegularFile, Hash(OriginalContent),
                    "device:test;inode:original-target", 1, [],
                    "utf-8", false, "lf", "0644", false, false, null)
                : ChangePlanPathState.Missing;
            var directory = new ChangePlanPathState(
                true, ChangePlanPathKind.Directory, null, "device:test;inode:src", 2, [],
                null, null, null, "0755", true, false, null);
            var operations = new List<ChangePlanOperation>
            {
                new(1, replacement ? "replace" : "create", sourcePath, [])
            };
            var effects = new List<ChangePlanEffect>
            {
                new(1, replacement ? ChangePlanEffectKind.Replace : ChangePlanEffectKind.Create,
                    ChangePlanArtifactKind.RoslynDocument,
                    sourcePath, TargetPath, null, null,
                    before, after, null, null, replacement ? OriginalContent : null, Content,
                    [new ChangePlanGuardRequirement(
                        sourcePath, ChangePlanGuardKind.ExternalWriteGuard, "guard-target")])
            };
            if (twoEffects)
            {
                const string secondPath = "src/Added2.cs";
                var secondBefore = secondReplacement
                    ? new ChangePlanPathState(
                        true, ChangePlanPathKind.RegularFile, Hash(OriginalContent),
                        "device:test;inode:original-target-2", 1, [],
                        "utf-8", false, "lf", "0644", false, false, null)
                    : ChangePlanPathState.Missing;
                operations.Add(new ChangePlanOperation(
                    2, secondReplacement ? "replace" : "create", secondPath, []));
                effects.Add(new ChangePlanEffect(
                    2, secondReplacement ? ChangePlanEffectKind.Replace : ChangePlanEffectKind.Create,
                    ChangePlanArtifactKind.RoslynDocument,
                    secondPath, Path.Combine(RepositoryRoot, "src", "Added2.cs"), null, null,
                    secondBefore,
                    after, null, null, secondReplacement ? OriginalContent : null, Content,
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

    private sealed class FailGuardJournalReplace(OperationGuardJournalKind failedKind)
        : IOperationLedgerDurability
    {
        public void FlushFileMetadata(string path)
        {
        }

        public void ReplaceFile(string temporaryPath, string recordPath)
        {
            var guardJournal = JsonNode.Parse(File.ReadAllText(temporaryPath))?
                ["guardJournal"]?.AsArray();
            if (guardJournal is { Count: > 0 }
                && guardJournal[^1]?["kind"]?.GetValue<int>()
                == (int)failedKind)
                throw new IOException("Injected guard journal record failure.");
            File.Move(temporaryPath, recordPath, overwrite: true);
        }

        public void FlushDirectoryMetadata(string directory)
        {
        }
    }

    private sealed class FailPlanReplace : IChangePlanStoreDurability
    {
        public void FlushFileMetadata(string path)
        {
        }

        public void ReplaceFile(string temporaryPath, string recordPath) =>
            throw new IOException("Injected plan pin release replacement failure.");

        public void FlushDirectoryMetadata(string directory)
        {
        }
    }
}
