namespace RoslynTools.Core;

internal enum StagingArtifactErrorKind
{
    LedgerUnavailable,
    UnsafeRoot,
    ArtifactUnavailable
}

internal sealed record StagingArtifactError(
    StagingArtifactErrorKind Kind,
    string Message,
    Exception? Cause = null);

internal interface IStagingArtifactBoundary
{
    IStagingArtifactSession Acquire(StoredChangePlan plan);
}

internal interface IStagingArtifactSession : IDisposable
{
    Task<string> CreateAndHashAsync(
        OperationStagingJournalEvent creation,
        ReadOnlyMemory<byte> replacementBytes,
        bool allowExistingAdmittedArtifact);
}

internal sealed class JournaledStagingArtifactWriter
{
    private readonly DurableOperationLedger _ledger;
    private readonly IStagingArtifactBoundary _boundary;

    internal JournaledStagingArtifactWriter(
        DurableOperationLedger ledger,
        IStagingArtifactBoundary boundary)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _boundary = boundary ?? throw new ArgumentNullException(nameof(boundary));
    }

    internal async Task<Result<OperationStagingJournalEvent, StagingArtifactError>> StageReplacementAsync(
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
        reservation.AssertHeld(plan.Key.RepositoryId);

        IStagingArtifactSession session;
        try
        {
            session = _boundary.Acquire(plan);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException
                    or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return Result<OperationStagingJournalEvent, StagingArtifactError>.Err(new(
                StagingArtifactErrorKind.UnsafeRoot,
                "The protected staging root or same-volume proof is unavailable.", exception));
        }

        using (session)
        {
            var loaded = _ledger.Load(plan.Key.RepositoryId, operationId);
            if (loaded.IsErr)
                return LedgerFailure(loaded.Match(_ => null!, error => error));
            var pendingCreation = loaded.Match(
                operation => !operation.StagingJournal.IsEmpty
                             && operation.StagingJournal[^1].Kind
                                 == OperationStagingJournalKind.CreationAdmitted
                             && operation.StagingJournal[^1].EffectSequence == effectSequence,
                _ => false);
            if (!pendingCreation)
            {
                var intent = _ledger.AppendStagingIntent(
                    plan, operationId, effectSequence, admission, reservation);
                if (intent.IsErr)
                    return LedgerFailure(intent.Match(_ => null!, error => error));
            }
            var creation = _ledger.AppendStagingCreationAdmission(
                plan, operationId, effectSequence, admission, reservation);
            if (creation.IsErr)
                return LedgerFailure(creation.Match(_ => null!, error => error));
            var admitted = creation.Match(value => value, _ => null!);
            var effect = plan.Envelope.Payload.Effects.Single(candidate =>
                candidate.Sequence == effectSequence && candidate.HasReplacementContent);
            string observedHash;
            try
            {
                observedHash = await session.CreateAndHashAsync(
                    admitted, effect.ReplacementContent.ToArray(), pendingCreation);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException
                        or UnauthorizedAccessException)
            {
                return Result<OperationStagingJournalEvent, StagingArtifactError>.Err(new(
                    StagingArtifactErrorKind.ArtifactUnavailable,
                    "The admitted staging artifact was not durably created and verified.", exception));
            }
            var verified = _ledger.AppendStagingVerification(
                plan, operationId, effectSequence, observedHash,
                admitted.CreationToken!.Value, admission, reservation);
            if (verified.IsErr)
                return LedgerFailure(verified.Match(_ => null!, error => error));
            return Result<OperationStagingJournalEvent, StagingArtifactError>.Ok(
                verified.Match(value => value, _ => null!).StagingJournal[^1]);
        }
    }

    private static Result<OperationStagingJournalEvent, StagingArtifactError> LedgerFailure(
        OperationLedgerError error) =>
        Result<OperationStagingJournalEvent, StagingArtifactError>.Err(new(
            StagingArtifactErrorKind.LedgerUnavailable, error.Message));
}
