using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace RoslynTools.Core;

internal enum JournaledApplyErrorKind
{
    PlanUnavailable,
    LedgerUnavailable,
    UnsafeCoordination,
    CleanupRequired,
    RecoveryRequired
}

internal sealed record JournaledApplyError(
    JournaledApplyErrorKind Kind,
    string Message,
    Exception? Cause = null);

/// <summary>
///     Trusted host capability. A session must hold an OS-enforced external-writer guard
///     over the entire reviewed plan, or make every effect conditional on all reviewed
///     dependency tokens. ApplyEffectAsync must atomically check its one-use admission
///     against the still-held reservation and durable intent at the filesystem boundary.
///     AcquireAsync receives the current pre-effect activity and must reject a closed
///     generation at the actual guard-acquisition boundary.
///     A null result proves no guard remains held. An exception has no such
///     meaning: the caller retains the activity until guard release is proved.
///     A host without this capability must return no session.
/// </summary>
internal interface IJournaledRepositoryEffectBroker
{
    Task<IJournaledRepositoryEffectSession?> AcquireAsync(
        StoredChangePlan plan,
        PreEffectActivityAdmission activity,
        RepositoryMutationReservation reservation,
        CancellationToken cancellationToken);
}

internal interface IJournaledRepositoryEffectSession : IAsyncDisposable
{
    Task<GuardedPreflightEvidence?> FinalPreflightAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Performs only the effect named by the durable intent and returns a verified
    ///     observation digest. It must reject stale reservation or dependency tokens.
    /// </summary>
    Task<string> ApplyEffectAsync(
        ChangePlanEffect effect,
        RepositoryEffectAdmission admission,
        CancellationToken cancellationToken);

    /// <summary>Checks all plan postconditions while guards and reservation remain held.</summary>
    Task<string?> VerifyAppliedAsync(CancellationToken cancellationToken);
}

internal sealed record GuardedPreflightEvidence(
    ImmutableArray<OperationGuardToken> GuardTokens);

internal static class OperationGuardTokenSet
{
    internal static ImmutableArray<OperationGuardToken> Expected(
        ChangePlanPayload plan) =>
        plan.Dependencies.Select(dependency => dependency.Guard)
            .Concat(plan.Effects.SelectMany(effect => effect.Guards))
            .Select(guard => new OperationGuardToken(
                guard.Path,
                guard.Kind,
                guard.ExpectedToken))
            .Distinct()
            .OrderBy(guard => guard.Path, StringComparer.Ordinal)
            .ThenBy(guard => guard.Kind)
            .ThenBy(guard => guard.VersionToken, StringComparer.Ordinal)
            .ToImmutableArray();
}

internal sealed class UnavailableJournaledRepositoryEffectBroker : IJournaledRepositoryEffectBroker
{
    internal static UnavailableJournaledRepositoryEffectBroker Instance { get; } = new();

    /// <summary>
    ///     Returns no effect session for any plan, activity, or reservation. The
    ///     default host therefore cannot make a repository effect.
    /// </summary>
    public Task<IJournaledRepositoryEffectSession?> AcquireAsync(
        StoredChangePlan plan,
        PreEffectActivityAdmission activity,
        RepositoryMutationReservation reservation,
        CancellationToken cancellationToken) =>
        Task.FromResult<IJournaledRepositoryEffectSession?>(null);
}

internal static class OperationEffectIntentIdentity
{
    internal static string Compute(
        string planHash,
        int effectSequence,
        Guid fenceToken,
        long fenceEpoch) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(
            $"{planHash}:{effectSequence}:{fenceEpoch}:{fenceToken:N}"))).ToLowerInvariant();
}

/// <summary>
///     One-use admission for a single durable intent. The trusted effect broker
///     calls Admit at the actual guarded filesystem boundary before the effect.
/// </summary>
internal sealed class RepositoryEffectAdmission
{
    private readonly DurableOperationLedger _ledger;
    private readonly RepositoryMutationReservation _reservation;
    private readonly string _repositoryId;
    private readonly OperationId _operationId;
    private readonly int _effectSequence;
    private int _attempted;

    internal RepositoryEffectAdmission(
        DurableOperationLedger ledger,
        RepositoryMutationReservation reservation,
        string repositoryId,
        OperationId operationId,
        int effectSequence,
        string intentHash)
    {
        _ledger = ledger;
        _reservation = reservation;
        _repositoryId = repositoryId;
        _operationId = operationId;
        _effectSequence = effectSequence;
        IntentHash = intentHash;
    }

    internal string IntentHash { get; }

    internal void Admit()
    {
        if (Interlocked.Exchange(ref _attempted, 1) != 0)
            throw new InvalidOperationException("An effect admission cannot be reused.");
        _reservation.AssertHeld(_repositoryId);
        var admitted = _ledger.AppendEffectAdmission(
            _repositoryId,
            _operationId,
            _effectSequence,
            IntentHash,
            _reservation);
        if (admitted.IsErr)
        {
            var error = admitted.Match(_ => null!, value => value);
            throw new IOException("The guarded effect admission was not durable: " + error.Message);
        }
    }
}

/// <summary>
///     Internal authority for an exact stored plan. No repository writes occur here:
///     only the trusted effect session can request a guarded filesystem effect.
/// </summary>
internal sealed class JournaledApplyAuthority
{
    private readonly DurableChangePlanStore _planStore;
    private readonly DurableOperationLedger _ledger;
    private readonly RepositoryMutationReservationProvider _reservations;
    private readonly IJournaledRepositoryEffectBroker _broker;
    private readonly IStagingArtifactBoundary? _stagingBoundary;
    private readonly IBackupArtifactBoundary? _backupBoundary;
    private readonly JournaledNoEffectCleanup? _noEffectCleanup;

    internal JournaledApplyAuthority(
        DurableChangePlanStore planStore,
        DurableOperationLedger ledger,
        RepositoryMutationReservationProvider reservations,
        IJournaledRepositoryEffectBroker? broker = null,
        IStagingArtifactBoundary? stagingBoundary = null,
        IBackupArtifactBoundary? backupBoundary = null,
        JournaledNoEffectCleanup? noEffectCleanup = null)
    {
        _planStore = planStore;
        _ledger = ledger;
        _reservations = reservations;
        _broker = broker ?? UnavailableJournaledRepositoryEffectBroker.Instance;
        _stagingBoundary = stagingBoundary;
        _backupBoundary = backupBoundary;
        _noEffectCleanup = noEffectCleanup;
    }

    internal async Task<Result<DurableOperationRecord, JournaledApplyError>> ApplyAsync(
        CanonicalApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = new ChangePlanStoreKey(
            request.RepositoryId,
            request.Identity.PlanId,
            request.Identity.PlanHash);
        var loaded = _planStore.Load(key);
        if (loaded.IsErr)
        {
            var error = loaded.Match(_ => null!, value => value);
            return Fail(JournaledApplyErrorKind.PlanUnavailable, error.Message);
        }
        var plan = loaded.Match(value => value, _ => null!);
        if (!plan.Pins.Any(pin =>
                pin.Kind == ChangePlanPinKind.OperationLedger
                && string.Equals(
                    pin.ReferenceId,
                    DurableDispatchIntakeCoordinator.FormatOperationReference(request.OperationId),
                    StringComparison.Ordinal))
            || !string.Equals(
                plan.Envelope.Payload.Repository.BaseRevision,
                request.Identity.ExpectedRevision,
                StringComparison.Ordinal))
        {
            return Fail(JournaledApplyErrorKind.PlanUnavailable,
                "The exact stored plan is not pinned to this operation and revision.");
        }

        RepositoryMutationReservation reservation;
        try
        {
            reservation = await _reservations.AcquireAsync(
                plan.Envelope.Payload.Repository,
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return Fail(JournaledApplyErrorKind.UnsafeCoordination,
                "The repository mutation reservation could not be proved.", exception);
        }

        using (reservation)
        {
            reservation.AssertHeld(request.RepositoryId);
            var operationResult = _ledger.Load(request.RepositoryId, request.OperationId);
            if (operationResult.IsErr)
            {
                var error = operationResult.Match(_ => null!, value => value);
                return Fail(JournaledApplyErrorKind.LedgerUnavailable, error.Message);
            }
            var operation = operationResult.Match(value => value, _ => null!);
            if (operation.CurrentState != OperationLifecycleState.Prepared
                || !string.Equals(
                    operation.RequestIdentity.RequestHash,
                    request.Identity.RequestHash,
                    StringComparison.Ordinal))
            {
                return Fail(JournaledApplyErrorKind.LedgerUnavailable,
                    "The operation is not the prepared exact request.");
            }
            if (!operation.PreEffectActivity.ActiveAdmissions.IsEmpty)
            {
                return Fail(JournaledApplyErrorKind.RecoveryRequired,
                    "An earlier pre-effect activity has not been durably drained.");
            }

            var repositoryBarrier = _ledger.HasUnresolvedRepositoryMutation(
                request.RepositoryId,
                request.OperationId);
            if (repositoryBarrier.IsErr)
            {
                var error = repositoryBarrier.Match(_ => null!, value => value);
                return Fail(JournaledApplyErrorKind.LedgerUnavailable, error.Message);
            }
            if (repositoryBarrier.Match(value => value, _ => false))
            {
                return Fail(JournaledApplyErrorKind.RecoveryRequired,
                    "Another repository mutation remains unresolved.");
            }

            if (_broker is UnavailableJournaledRepositoryEffectBroker)
            {
                return Fail(JournaledApplyErrorKind.UnsafeCoordination,
                    "No OS-enforced repository effect broker is available.");
            }
            if ((plan.Envelope.Payload.Effects.Any(effect => effect.HasReplacementContent)
                 && _stagingBoundary is null)
                || (plan.Envelope.Payload.Effects.Any(effect => effect.HasOriginalContent)
                    && _backupBoundary is null))
            {
                return Fail(JournaledApplyErrorKind.UnsafeCoordination,
                    "The protected staging or backup artifact authority is unavailable.");
            }

            var begun = _ledger.BeginPreEffectActivity(
                request.RepositoryId,
                request.OperationId,
                reservation);
            if (begun.IsErr)
            {
                var error = begun.Match(_ => null!, value => value);
                return Fail(JournaledApplyErrorKind.LedgerUnavailable,
                    "Final preflight could not enter the durable pre-effect activity fence: "
                    + error.Message);
            }
            var activity = begun.Match(value => value, _ => null!);
            foreach (var effect in plan.Envelope.Payload.Effects)
            {
                if (effect.HasReplacementContent)
                {
                    var staged = await new JournaledStagingArtifactWriter(
                            _ledger, _stagingBoundary!)
                        .StageReplacementAsync(
                            plan, request.OperationId, effect.Sequence, activity, reservation);
                    if (staged.IsErr)
                    {
                        var error = staged.Match(_ => null!, value => value);
                        return await ReleaseAndDrainPreEffectAsync(
                            request, activity, reservation, null,
                            error.Kind == StagingArtifactErrorKind.UnsafeRoot
                                ? JournaledApplyErrorKind.UnsafeCoordination
                                : JournaledApplyErrorKind.LedgerUnavailable,
                            "Replacement staging failed before guard acquisition: " + error.Message,
                            error.Cause);
                    }
                }
                if (effect.HasOriginalContent)
                {
                    var backedUp = await new JournaledBackupArtifactWriter(
                            _ledger, _backupBoundary!)
                        .BackupOriginalAsync(
                            plan, request.OperationId, effect.Sequence, activity, reservation);
                    if (backedUp.IsErr)
                    {
                        var error = backedUp.Match(_ => null!, value => value);
                        return await ReleaseAndDrainPreEffectAsync(
                            request, activity, reservation, null,
                            error.Kind == BackupArtifactErrorKind.UnsafeRoot
                                ? JournaledApplyErrorKind.UnsafeCoordination
                                : JournaledApplyErrorKind.LedgerUnavailable,
                            "Original backup failed before guard acquisition: " + error.Message,
                            error.Cause);
                    }
                }
            }
            var beforeGuard = _ledger.Load(request.RepositoryId, request.OperationId);
            if (beforeGuard.IsErr)
            {
                var error = beforeGuard.Match(_ => null!, value => value);
                return await ReleaseAndDrainPreEffectAsync(
                    request, activity, reservation, null,
                    JournaledApplyErrorKind.LedgerUnavailable,
                    "The pre-effect fence could not be rechecked before guard acquisition: "
                    + error.Message);
            }
            var preGuardOperation = beforeGuard.Match(value => value, _ => null!);
            if (preGuardOperation.CurrentState == OperationLifecycleState.CancelledNoEffect)
            {
                return await ReleaseAndDrainPreEffectAsync(
                    request, activity, reservation, null,
                    JournaledApplyErrorKind.LedgerUnavailable,
                    "Cancellation closed the pre-effect fence before guard acquisition.");
            }
            if (preGuardOperation.CurrentState != OperationLifecycleState.Prepared
                || preGuardOperation.PreEffectActivity.IsClosed
                || !preGuardOperation.PreEffectActivity.ActiveAdmissions.Contains(activity))
            {
                return Fail(JournaledApplyErrorKind.RecoveryRequired,
                    "The pre-effect activity is no longer current before guard acquisition.");
            }
            IJournaledRepositoryEffectSession? session;
            try
            {
                session = await _broker.AcquireAsync(plan, activity, reservation, cancellationToken);
            }
            catch (Exception exception)
            {
                return Fail(JournaledApplyErrorKind.UnsafeCoordination,
                    "Guard acquisition did not return a releasable session; its activity remains active.",
                    exception);
            }
            GuardedPreflightEvidence? preflight = null;
            Exception? preflightError = null;
            try
            {
                if (session is not null)
                    preflight = await session.FinalPreflightAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                preflightError = exception;
            }
            if (preflightError is not null || session is null || preflight is null
                || !preflight.GuardTokens.SequenceEqual(
                    OperationGuardTokenSet.Expected(plan.Envelope.Payload)))
            {
                var message = preflightError is not null
                    ? "The required repository effect guard or preflight failed."
                    : session is null
                        ? "No OS-enforced repository effect broker is available."
                        : "Final guarded preflight did not prove every plan dependency.";
                return await ReleaseAndDrainPreEffectAsync(
                    request, activity, reservation, session,
                    JournaledApplyErrorKind.UnsafeCoordination, message, preflightError,
                    OperationLifecycleState.RejectedUnsafeCoordination);
            }

            var handoff = _ledger.BeginApplyingWithFirstEffectIntent(
                plan, request.OperationId, activity, reservation, preflight.GuardTokens);
            if (handoff.IsErr)
            {
                var error = handoff.Match(_ => null!, value => value);
                var visible = _ledger.Load(request.RepositoryId, request.OperationId);
                if (visible.IsErr
                    || !visible.Match(value => value, _ => null!).EffectJournal.IsEmpty)
                {
                    // A failed directory flush can leave the atomic handoff
                    // visible. No effect has run, but its durable intent must
                    // be reconciled before another owner enters.
                    return await ReleasePreEffectSessionAfterUncertainHandoffAsync(
                        session, error);
                }
                return await ReleaseAndDrainPreEffectAsync(
                    request, activity, reservation, session,
                    JournaledApplyErrorKind.LedgerUnavailable,
                    "The first-effect guard handoff was not durable: " + error.Message);
            }
            var handedOff = handoff.Match(value => value, _ => null!);
            if (handedOff.CurrentState == OperationLifecycleState.CancelledNoEffect)
            {
                return await ReleaseAndDrainPreEffectAsync(
                    request, activity, reservation, session,
                    JournaledApplyErrorKind.LedgerUnavailable,
                    "Cancellation retained the pre-effect guard activity.");
            }

            await using (session)
            {
                try
                {
                    foreach (var effect in plan.Envelope.Payload.Effects)
                    {
                        reservation.AssertHeld(request.RepositoryId);
                        var renewed = _ledger.RenewApplyOwner(
                            request.RepositoryId,
                            request.OperationId,
                            reservation);
                        if (renewed.IsErr)
                        {
                            var error = renewed.Match(_ => null!, value => value);
                            throw new IOException("The apply owner lease was not renewed: " + error.Message);
                        }
                        var intentHash = OperationEffectIntentIdentity.Compute(
                            request.Identity.PlanHash,
                            effect.Sequence,
                            reservation.FenceToken,
                            reservation.FenceEpoch);
                        var intent = _ledger.AppendEffectIntent(
                            request.RepositoryId,
                            request.OperationId,
                            effect.Sequence,
                            intentHash,
                            reservation,
                            CanonicalChangePlan.ComputeEffectHash(effect),
                            preflight.GuardTokens);
                        if (intent.IsErr)
                        {
                            var error = intent.Match(_ => null!, value => value);
                            throw new IOException("The effect intent was not durable: " + error.Message);
                        }

                        var admission = new RepositoryEffectAdmission(
                            _ledger,
                            reservation,
                            request.RepositoryId,
                            request.OperationId,
                            effect.Sequence,
                            intentHash);
                        var observationHash = await session.ApplyEffectAsync(
                            effect,
                            admission,
                            CancellationToken.None);
                        var observed = _ledger.AppendEffectObservation(
                            request.RepositoryId,
                            request.OperationId,
                            effect.Sequence,
                            observationHash,
                            reservation);
                        if (observed.IsErr)
                        {
                            var error = observed.Match(_ => null!, value => value);
                            throw new IOException("The effect observation was not durable: " + error.Message);
                        }
                    }

                    var verificationHash = await session.VerifyAppliedAsync(CancellationToken.None);
                    if (verificationHash is null)
                        throw new IOException("The guarded plan postconditions were not verified.");
                    var finalRenewal = _ledger.RenewApplyOwner(
                        request.RepositoryId,
                        request.OperationId,
                        reservation);
                    if (finalRenewal.IsErr)
                    {
                        var error = finalRenewal.Match(_ => null!, value => value);
                        throw new IOException("The final apply owner lease was not renewed: " + error.Message);
                    }
                    var applied = _ledger.AppendVerifiedApplied(
                        plan,
                        request.OperationId,
                        reservation,
                        verificationHash);
                    if (applied.IsErr)
                    {
                        // Atomic replacement can succeed before its directory flush
                        // reports a failure. A readback alone is not a durability proof.
                        var confirmed = _ledger.ConfirmDurableApplied(
                            request.RepositoryId,
                            request.OperationId,
                            verificationHash);
                        if (confirmed.IsOk)
                        {
                            return Result<DurableOperationRecord, JournaledApplyError>.Ok(
                                confirmed.Match(value => value, _ => null!));
                        }
                        var visible = _ledger.Load(request.RepositoryId, request.OperationId);
                        if (visible.IsOk
                            && visible.Match(value => value, _ => null!).CurrentState
                                == OperationLifecycleState.Applied)
                        {
                            return Fail(JournaledApplyErrorKind.LedgerUnavailable,
                                "The applied record is visible but its directory durability could not be proved; later mutation must revalidate it.");
                        }
                        var error = applied.Match(_ => null!, value => value);
                        throw new IOException("The verified applied outcome was not durable: " + error.Message);
                    }
                    return Result<DurableOperationRecord, JournaledApplyError>.Ok(
                        applied.Match(value => value, _ => null!));
                }
                catch (Exception exception)
                {
                    var current = _ledger.Load(request.RepositoryId, request.OperationId);
                    if (current.IsOk
                        && current.Match(value => value, _ => null!).CurrentState
                            == OperationLifecycleState.CancelledNoEffect)
                    {
                        return ConfirmCancelledNoEffect(request);
                    }
                    // A durable intent conservatively means its effect may have run.
                    // Keep the plan pin and journal for a later guarded reconciler.
                    _ledger.AppendTransition(
                        request.RepositoryId,
                        request.OperationId,
                        OperationLifecycleState.Applying,
                        OperationLifecycleState.RecoveryRequired,
                        "The guarded effect sequence needs reconciliation.");
                    return Fail(JournaledApplyErrorKind.RecoveryRequired,
                        "The effect outcome is unresolved and requires guarded recovery.",
                        exception);
                }
            }
        }
    }

    private async Task<Result<DurableOperationRecord, JournaledApplyError>>
        ReleaseAndDrainPreEffectAsync(
            CanonicalApplyRequest request,
            PreEffectActivityAdmission activity,
            RepositoryMutationReservation reservation,
            IJournaledRepositoryEffectSession? session,
            JournaledApplyErrorKind failureKind,
            string failureMessage,
            Exception? failureCause = null,
            OperationLifecycleState? terminalOutcome = null)
    {
        try
        {
            if (session is not null)
                await session.DisposeAsync();
        }
        catch (Exception exception)
        {
            return Fail(JournaledApplyErrorKind.UnsafeCoordination,
                "The pre-effect guard could not be released; its activity remains active.",
                CombineFailureCauses(failureCause, exception));
        }

        var completed = _ledger.CompletePreEffectActivity(
            request.RepositoryId, request.OperationId, activity, reservation,
            terminalOutcome);
        if (completed.IsErr)
        {
            var error = completed.Match(_ => null!, value => value);
            return Fail(JournaledApplyErrorKind.LedgerUnavailable,
                "Released pre-effect activity could not be durably resolved: " + error.Message,
                failureCause);
        }
        if (completed.Match(value => value, _ => null!).CurrentState
            == OperationLifecycleState.CancelledNoEffect)
        {
            return ConfirmCancelledNoEffect(request, reservation, failureCause);
        }
        if (terminalOutcome is not null)
        {
            if (completed.Match(value => value, _ => null!).CurrentState != terminalOutcome)
            {
                return Fail(JournaledApplyErrorKind.LedgerUnavailable,
                    "The no-effect guard rejection did not reach its terminal state.");
            }
            if (_noEffectCleanup is not null)
            {
                var cleaned = _noEffectCleanup.Cleanup(
                    request.RepositoryId, request.OperationId, reservation);
                if (cleaned.IsErr)
                {
                    var error = cleaned.Match(_ => null!, value => value);
                    return Fail(JournaledApplyErrorKind.CleanupRequired,
                        "The no-effect guard rejection is durable but cleanup remains pending: "
                        + failureMessage + " Cleanup failed: " + error.Message,
                        CombineFailureCauses(failureCause, error.Cause));
                }
            }
        }
        return Fail(failureKind, failureMessage, failureCause);
    }

    private static async Task<Result<DurableOperationRecord, JournaledApplyError>>
        ReleasePreEffectSessionAfterUncertainHandoffAsync(
            IJournaledRepositoryEffectSession session,
            OperationLedgerError handoffError)
    {
        try
        {
            await session.DisposeAsync();
        }
        catch (Exception exception)
        {
            return Fail(JournaledApplyErrorKind.UnsafeCoordination,
                "The first-effect handoff and guard release could not be proved.", exception);
        }
        return Fail(JournaledApplyErrorKind.LedgerUnavailable,
            "The first-effect handoff may be visible but its durability is uncertain: "
            + handoffError.Message);
    }

    private Result<DurableOperationRecord, JournaledApplyError> ConfirmCancelledNoEffect(
        CanonicalApplyRequest request,
        RepositoryMutationReservation? reservation = null,
        Exception? earlierFailure = null)
    {
        var confirmed = _ledger.ConfirmDurableCancelledNoEffect(
            request.RepositoryId,
            request.OperationId);
        if (confirmed.IsErr)
        {
            var error = confirmed.Match(_ => null!, value => value);
            return Fail(JournaledApplyErrorKind.LedgerUnavailable,
                "The no-effect cancellation could not be durably confirmed: " + error.Message,
                earlierFailure);
        }
        if (_noEffectCleanup is null || reservation is null)
            return Result<DurableOperationRecord, JournaledApplyError>.Ok(
                confirmed.Match(value => value, _ => null!));
        var cleaned = _noEffectCleanup.Cleanup(
            request.RepositoryId, request.OperationId, reservation);
        if (cleaned.IsErr)
        {
            var error = cleaned.Match(_ => null!, value => value);
            return Fail(JournaledApplyErrorKind.CleanupRequired,
                "No-effect cancellation is durable but cleanup remains pending: "
                + error.Message, CombineFailureCauses(earlierFailure, error.Cause));
        }
        return Result<DurableOperationRecord, JournaledApplyError>.Ok(
            cleaned.Match(value => value, _ => null!));
    }

    private static Exception? CombineFailureCauses(Exception? primary, Exception? secondary) =>
        primary is null ? secondary
        : secondary is null ? primary
        : new AggregateException(primary, secondary);

    private static Result<DurableOperationRecord, JournaledApplyError> Fail(
        JournaledApplyErrorKind kind,
        string message,
        Exception? cause = null) =>
        Result<DurableOperationRecord, JournaledApplyError>.Err(new(kind, message, cause));
}
