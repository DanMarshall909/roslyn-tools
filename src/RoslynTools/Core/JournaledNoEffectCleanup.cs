namespace RoslynTools.Core;

internal enum NoEffectCleanupErrorKind
{
    LedgerUnavailable,
    PlanUnavailable,
    NotReady,
    UnresolvedGuard,
    UnsafeRoot,
    ArtifactUnavailable,
    PinReleaseUnavailable
}

internal sealed record NoEffectCleanupError(
    NoEffectCleanupErrorKind Kind,
    string Message,
    Exception? Cause = null);

/// <summary>
///     Cleans only no-effect control artifacts whose admission fence has drained.
///     The durable inventory authorizes removals; each completed removal and the
///     empty operation root are recorded before releasing the plan pin.
/// </summary>
internal sealed class JournaledNoEffectCleanup
{
    private readonly DurableOperationLedger _ledger;
    private readonly DurableChangePlanStore _store;
    private readonly LinuxStagingCleanupBoundary _stagingBoundary;
    private readonly LinuxBackupCleanupBoundary? _backupBoundary;

    internal JournaledNoEffectCleanup(
        DurableOperationLedger ledger,
        DurableChangePlanStore store,
        LinuxStagingCleanupBoundary stagingBoundary,
        LinuxBackupCleanupBoundary? backupBoundary = null)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _stagingBoundary = stagingBoundary ?? throw new ArgumentNullException(nameof(stagingBoundary));
        _backupBoundary = backupBoundary;
    }

    internal Result<DurableOperationRecord, NoEffectCleanupError> Cleanup(
        string repositoryId,
        OperationId operationId,
        RepositoryMutationReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(reservation);
        using var controlActivity = reservation.HoldControlActivity(repositoryId);

        var loaded = _ledger.Load(repositoryId, operationId);
        if (loaded.IsErr)
            return LedgerFailure(loaded.Match(_ => null!, error => error));
        var operation = loaded.Match(value => value, _ => null!);
        if (!DurableOperationLedger.CanCleanupNoEffect(operation))
        {
            var reconciled = _ledger.ReconcileInterruptedPreEffectNoEffect(
                repositoryId, operationId, reservation);
            if (reconciled.IsErr)
            {
                var error = reconciled.Match(_ => null!, value => value);
                return error.Kind == OperationLedgerErrorKind.InvalidTransition
                    ? Fail(NoEffectCleanupErrorKind.NotReady,
                        "No-effect cleanup requires a closed and drained pre-effect activity fence.")
                    : LedgerFailure(error);
            }
            operation = reconciled.Match(value => value, _ => null!);
        }
        var confirmed = _ledger.ConfirmDurableNoEffectCleanupReady(repositoryId, operationId);
        if (confirmed.IsErr)
            return LedgerFailure(confirmed.Match(_ => null!, error => error));
        operation = confirmed.Match(value => value, _ => null!);
        if (operation.CleanupPinReleased)
        {
            return _ledger.CanCleanupUnderReservation(operation, reservation)
                ? Result<DurableOperationRecord, NoEffectCleanupError>.Ok(operation)
                : Fail(NoEffectCleanupErrorKind.NotReady,
                    "Verified cleanup requires the original reservation or a verified local successor.");
        }
        // The artifact journals can be partial. Bind and scan both operation
        // roots from the exact retained plan before recording CleanupVerified.
        var key = new ChangePlanStoreKey(
            operation.RepositoryId,
            operation.RequestIdentity.PlanId,
            operation.RequestIdentity.PlanHash);
        var loadedPlan = _store.LoadRetained(key);
        if (loadedPlan.IsErr)
            return Fail(NoEffectCleanupErrorKind.PlanUnavailable,
                "The exact retained plan is unavailable for cleanup root verification.");
        var storedPlan = loadedPlan.Match(value => value, _ => null!);
        if (operation.CleanupJournal[^1].Kind != OperationCleanupJournalKind.CleanupVerified
            && !storedPlan.Pins.Any(pin =>
                pin.Kind == ChangePlanPinKind.OperationLedger
                && pin.ReferenceId == DurableDispatchIntakeCoordinator.FormatOperationReference(operationId)))
        {
            return Fail(NoEffectCleanupErrorKind.PlanUnavailable,
                "The exact retained plan has no operation pin for cleanup.");
        }
        var coordination = storedPlan.Envelope.Payload.Coordination;
        var backupBoundary = _backupBoundary ?? new LinuxBackupCleanupBoundary(coordination.BackupRoot);
        if (!string.Equals(coordination.StagingRoot, _stagingBoundary.TrustedRoot,
                StringComparison.Ordinal)
            || !string.Equals(coordination.BackupRoot, backupBoundary.TrustedRoot,
                StringComparison.Ordinal))
        {
            return Fail(NoEffectCleanupErrorKind.UnsafeRoot,
                "Cleanup requires both roots from the exact retained plan.");
        }
        if (operation.StagingJournal.Any(entry =>
                !string.Equals(entry.StagingRoot, _stagingBoundary.TrustedRoot, StringComparison.Ordinal)))
        {
            return Fail(NoEffectCleanupErrorKind.UnsafeRoot,
                "The durable staging journal does not name the configured cleanup root.");
        }
        if (operation.BackupJournal.Any(entry =>
                !string.Equals(entry.BackupRoot, backupBoundary.TrustedRoot,
                    StringComparison.Ordinal)))
        {
            return Fail(NoEffectCleanupErrorKind.UnsafeRoot,
                "The durable backup journal does not name the configured cleanup root.");
        }

        if (!_ledger.CanCleanupUnderReservation(operation, reservation))
            return Fail(NoEffectCleanupErrorKind.NotReady,
                "Physical cleanup requires the original reservation or a verified local successor.");
        if (operation.CleanupJournal[^1].Kind == OperationCleanupJournalKind.CleanupVerified)
            return ReleasePin(operation, reservation, backupBoundary);

        var artifacts = operation.CleanupJournal[0].Artifacts;
        var removed = operation.CleanupJournal.Length - 1;
        LinuxControlCleanupBoundary.Session stagingSession;
        try
        {
            stagingSession = _stagingBoundary.Acquire(allowMissingRoot: !artifacts
                .Skip(removed).Any(artifact => artifact.Kind == OperationCleanupArtifactKind.Staging));
        }
        catch (Exception exception) when (IsBoundaryException(exception))
        {
            return Fail(NoEffectCleanupErrorKind.UnsafeRoot,
                "The protected staging root is unavailable for cleanup.", exception);
        }

        using (stagingSession)
        {
            LinuxControlCleanupBoundary.Session backupSession;
            try
            {
                backupSession = backupBoundary.Acquire(allowMissingRoot: !artifacts
                    .Skip(removed).Any(artifact => artifact.Kind == OperationCleanupArtifactKind.Backup));
            }
            catch (Exception exception) when (IsBoundaryException(exception))
            {
                return Fail(NoEffectCleanupErrorKind.UnsafeRoot,
                    "The protected backup root is unavailable for cleanup.", exception);
            }

            using (backupSession)
            {
                for (var index = removed; index < artifacts.Length; index++)
                {
                    var artifact = artifacts[index];
                    try
                    {
                        var session = artifact.Kind == OperationCleanupArtifactKind.Staging
                            ? stagingSession
                            : backupSession;
                        session.RemoveArtifact(operationId, artifact);
                    }
                    catch (Exception exception) when (IsBoundaryException(exception))
                    {
                        return Fail(NoEffectCleanupErrorKind.ArtifactUnavailable,
                            "The admitted control artifact could not be durably removed.", exception);
                    }
                    var recorded = _ledger.AppendCleanupArtifactRemoved(
                        repositoryId, operationId, artifact, reservation);
                    if (recorded.IsErr)
                        return LedgerFailure(recorded.Match(_ => null!, error => error));
                    operation = recorded.Match(value => value, _ => null!);
                }

                try
                {
                    stagingSession.RemoveEmptyOperationRoot(operationId);
                    backupSession.RemoveEmptyOperationRoot(operationId);
                }
                catch (Exception exception) when (IsBoundaryException(exception))
                {
                    return Fail(NoEffectCleanupErrorKind.ArtifactUnavailable,
                        "The protected operation roots are not durably empty.", exception);
                }
            }
        }

        var verified = _ledger.AppendCleanupVerified(repositoryId, operationId, reservation);
        if (verified.IsErr)
            return LedgerFailure(verified.Match(_ => null!, error => error));
        return ReleasePin(verified.Match(value => value, _ => null!), reservation, backupBoundary);
    }

    private Result<DurableOperationRecord, NoEffectCleanupError> ReleasePin(
        DurableOperationRecord operation,
        RepositoryMutationReservation reservation,
        LinuxBackupCleanupBoundary backupBoundary)
    {
        reservation.AssertHeld(operation.RepositoryId);
        LinuxControlCleanupBoundary.Session stagingSession;
        try
        {
            stagingSession = _stagingBoundary.Acquire(allowMissingRoot: true);
        }
        catch (Exception exception) when (IsBoundaryException(exception))
        {
            return Fail(NoEffectCleanupErrorKind.UnsafeRoot,
                "The protected staging root is unavailable for cleanup confirmation.", exception);
        }
        using (stagingSession)
        {
            LinuxControlCleanupBoundary.Session backupSession;
            try
            {
                backupSession = backupBoundary.Acquire(allowMissingRoot: true);
            }
            catch (Exception exception) when (IsBoundaryException(exception))
            {
                return Fail(NoEffectCleanupErrorKind.UnsafeRoot,
                    "The protected backup root is unavailable for cleanup confirmation.", exception);
            }
            using (backupSession)
            {
                try
                {
                    stagingSession.RemoveEmptyOperationRoot(operation.OperationId);
                    backupSession.RemoveEmptyOperationRoot(operation.OperationId);
                }
                catch (Exception exception) when (IsBoundaryException(exception))
                {
                    return Fail(NoEffectCleanupErrorKind.ArtifactUnavailable,
                        "The protected operation roots are not empty at pin release.", exception);
                }
            }
        }

        var confirmed = _ledger.ConfirmDurableCleanupVerified(
            operation.RepositoryId, operation.OperationId);
        if (confirmed.IsErr)
            return LedgerFailure(confirmed.Match(_ => null!, error => error));
        operation = confirmed.Match(value => value, _ => null!);
        var key = new ChangePlanStoreKey(
            operation.RepositoryId,
            operation.RequestIdentity.PlanId,
            operation.RequestIdentity.PlanHash);
        var released = _store.ReleasePin(
            key,
            ChangePlanPinKind.OperationLedger,
            DurableDispatchIntakeCoordinator.FormatOperationReference(operation.OperationId));
        if (released.IsErr)
        {
            var error = released.Match(_ => null!, value => value);
            if (error.Kind == ChangePlanStoreErrorKind.Missing)
                return Fail(NoEffectCleanupErrorKind.PlanUnavailable,
                    "The exact plan disappeared before its pin release was confirmed.");
            return Fail(NoEffectCleanupErrorKind.PinReleaseUnavailable,
                "Cleanup was verified but the plan pin could not be released: " + error.Message);
        }
        var confirmedRelease = _ledger.MarkCleanupPinReleased(
            operation.RepositoryId, operation.OperationId, reservation);
        return confirmedRelease.IsErr
            ? LedgerFailure(confirmedRelease.Match(_ => null!, error => error))
            : Result<DurableOperationRecord, NoEffectCleanupError>.Ok(
                confirmedRelease.Match(value => value, _ => null!));
    }

    private static Result<DurableOperationRecord, NoEffectCleanupError> LedgerFailure(
        OperationLedgerError error) => Fail(
            error.Kind == OperationLedgerErrorKind.UnresolvedGuard
                ? NoEffectCleanupErrorKind.UnresolvedGuard
                : NoEffectCleanupErrorKind.LedgerUnavailable,
            error.Message);

    private static Result<DurableOperationRecord, NoEffectCleanupError> Fail(
        NoEffectCleanupErrorKind kind,
        string message,
        Exception? cause = null) =>
        Result<DurableOperationRecord, NoEffectCleanupError>.Err(new(kind, message, cause));

    private static bool IsBoundaryException(Exception exception) =>
        exception is IOException or InvalidOperationException
            or UnauthorizedAccessException or PlatformNotSupportedException;
}
