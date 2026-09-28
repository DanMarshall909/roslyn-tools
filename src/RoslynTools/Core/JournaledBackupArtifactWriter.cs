namespace RoslynTools.Core;

internal enum BackupArtifactErrorKind
{
    LedgerUnavailable,
    UnsafeRoot,
    ArtifactUnavailable
}

internal sealed record BackupArtifactError(
    BackupArtifactErrorKind Kind,
    string Message,
    Exception? Cause = null);

internal interface IBackupArtifactBoundary
{
    IBackupArtifactSession Acquire(StoredChangePlan plan);
}

internal interface IBackupArtifactSession : IDisposable
{
    Task<string> CreateAndHashAsync(
        OperationBackupJournalEvent creation,
        ReadOnlyMemory<byte> originalBytes,
        bool allowExistingAdmittedArtifact);

    Task<string> VerifyExistingAsync(
        OperationBackupJournalEvent verification,
        ReadOnlyMemory<byte> originalBytes);
}

internal sealed class JournaledBackupArtifactWriter
{
    private readonly DurableOperationLedger _ledger;
    private readonly IBackupArtifactBoundary _boundary;

    internal JournaledBackupArtifactWriter(
        DurableOperationLedger ledger,
        IBackupArtifactBoundary boundary)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _boundary = boundary ?? throw new ArgumentNullException(nameof(boundary));
    }

    internal async Task<Result<OperationBackupJournalEvent, BackupArtifactError>> BackupOriginalAsync(
        StoredChangePlan plan,
        OperationId operationId,
        int effectSequence,
        PreEffectActivityAdmission admission,
        RepositoryMutationReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(reservation);
        using var controlActivity = reservation.HoldControlActivity(plan.Key.RepositoryId);

        IBackupArtifactSession session;
        try
        {
            session = _boundary.Acquire(plan);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException
                    or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return Result<OperationBackupJournalEvent, BackupArtifactError>.Err(new(
                BackupArtifactErrorKind.UnsafeRoot,
                "The protected backup root or same-volume proof is unavailable.", exception));
        }

        using (session)
        {
            var loaded = _ledger.Load(plan.Key.RepositoryId, operationId);
            if (loaded.IsErr)
                return LedgerFailure(loaded.Match(_ => null!, error => error));
            var last = loaded.Match(
                operation => operation.BackupJournal.IsEmpty
                    ? null
                    : operation.BackupJournal[^1],
                _ => null);
            var matchingLast = last?.EffectSequence == effectSequence;
            var verifiedReplay = matchingLast && last!.Kind == OperationBackupJournalKind.Verified;
            var pendingCreation = matchingLast
                                  && last!.Kind == OperationBackupJournalKind.CreationAdmitted;
            OperationBackupJournalEvent admitted;
            if (verifiedReplay)
            {
                // An idempotent verification append first confirms the original
                // activity and reservation under the ledger lock, before any
                // control-root file flush on this replay.
                var confirmed = _ledger.AppendBackupVerification(
                    plan, operationId, effectSequence, last!.ObservedHash!,
                    last.CreationToken!.Value, admission, reservation);
                if (confirmed.IsErr)
                    return LedgerFailure(confirmed.Match(_ => null!, error => error));
                admitted = confirmed.Match(value => value, _ => null!).BackupJournal[^1];
            }
            else
            {
                if (!pendingCreation)
                {
                    var intent = _ledger.AppendBackupIntent(
                        plan, operationId, effectSequence, admission, reservation);
                    if (intent.IsErr)
                        return LedgerFailure(intent.Match(_ => null!, error => error));
                }
                var creation = _ledger.AppendBackupCreationAdmission(
                    plan, operationId, effectSequence, admission, reservation);
                if (creation.IsErr)
                    return LedgerFailure(creation.Match(_ => null!, error => error));
                admitted = creation.Match(value => value, _ => null!);
            }
            var effect = plan.Envelope.Payload.Effects.Single(candidate =>
                candidate.Sequence == effectSequence && candidate.HasOriginalContent);
            string observedHash;
            try
            {
                observedHash = verifiedReplay
                    ? await session.VerifyExistingAsync(
                        admitted, effect.OriginalContent.ToArray())
                    : await session.CreateAndHashAsync(
                        admitted, effect.OriginalContent.ToArray(), pendingCreation);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException
                        or UnauthorizedAccessException)
            {
                return Result<OperationBackupJournalEvent, BackupArtifactError>.Err(new(
                    BackupArtifactErrorKind.ArtifactUnavailable,
                    "The admitted backup artifact was not durably created and verified.", exception));
            }
            var verified = _ledger.AppendBackupVerification(
                plan, operationId, effectSequence, observedHash,
                admitted.CreationToken!.Value, admission, reservation);
            if (verified.IsErr)
                return LedgerFailure(verified.Match(_ => null!, error => error));
            return Result<OperationBackupJournalEvent, BackupArtifactError>.Ok(
                verified.Match(value => value, _ => null!).BackupJournal[^1]);
        }
    }

    private static Result<OperationBackupJournalEvent, BackupArtifactError> LedgerFailure(
        OperationLedgerError error) =>
        Result<OperationBackupJournalEvent, BackupArtifactError>.Err(new(
            BackupArtifactErrorKind.LedgerUnavailable, error.Message));
}
