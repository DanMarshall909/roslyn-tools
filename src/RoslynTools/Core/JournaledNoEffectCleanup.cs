namespace RoslynTools.Core;

internal enum NoEffectCleanupErrorKind
{
    LedgerUnavailable,
    NotReady,
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
        reservation.AssertHeld(repositoryId);

        var loaded = _ledger.Load(repositoryId, operationId);
        if (loaded.IsErr)
            return LedgerFailure(loaded.Match(_ => null!, error => error));
        var operation = loaded.Match(value => value, _ => null!);
        if (!DurableOperationLedger.CanCleanupNoEffect(operation))
            return Fail(NoEffectCleanupErrorKind.NotReady,
                "No-effect cleanup requires a closed and drained pre-effect activity fence.");
        if (operation.StagingJournal.Any(entry =>
                !string.Equals(entry.StagingRoot, _stagingBoundary.TrustedRoot, StringComparison.Ordinal)))
        {
            return Fail(NoEffectCleanupErrorKind.UnsafeRoot,
                "The durable staging journal does not name the configured cleanup root.");
        }
        if (!operation.BackupJournal.IsEmpty
            && (_backupBoundary is null
                || operation.BackupJournal.Any(entry =>
                    !string.Equals(entry.BackupRoot, _backupBoundary.TrustedRoot,
                        StringComparison.Ordinal))))
        {
            return Fail(NoEffectCleanupErrorKind.UnsafeRoot,
                "The durable backup journal does not name the configured cleanup root.");
        }

        if (!_ledger.CanCleanupUnderReservation(operation, reservation))
            return Fail(NoEffectCleanupErrorKind.NotReady,
                "Physical cleanup requires the original reservation or a verified local successor.");
        if (operation.CleanupJournal[^1].Kind == OperationCleanupJournalKind.CleanupVerified)
            return ReleasePin(operation, reservation);

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
            LinuxControlCleanupBoundary.Session? backupSession;
            try
            {
                backupSession = _backupBoundary?.Acquire(allowMissingRoot: !artifacts
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
                            : backupSession ?? throw new InvalidOperationException(
                                "The admitted backup has no cleanup boundary.");
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
                    backupSession?.RemoveEmptyOperationRoot(operationId);
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
        return ReleasePin(verified.Match(value => value, _ => null!), reservation);
    }

    private Result<DurableOperationRecord, NoEffectCleanupError> ReleasePin(
        DurableOperationRecord operation,
        RepositoryMutationReservation reservation)
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
            LinuxControlCleanupBoundary.Session? backupSession;
            try
            {
                backupSession = _backupBoundary?.Acquire(allowMissingRoot: true);
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
                    backupSession?.RemoveEmptyOperationRoot(operation.OperationId);
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
                return Result<DurableOperationRecord, NoEffectCleanupError>.Ok(operation);
            return Fail(NoEffectCleanupErrorKind.PinReleaseUnavailable,
                "Cleanup was verified but the plan pin could not be released: " + error.Message);
        }
        return Result<DurableOperationRecord, NoEffectCleanupError>.Ok(operation);
    }

    private static Result<DurableOperationRecord, NoEffectCleanupError> LedgerFailure(
        OperationLedgerError error) => Fail(NoEffectCleanupErrorKind.LedgerUnavailable,
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
