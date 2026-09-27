using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace RoslynTools.Core;

internal enum JournaledApplyErrorKind
{
    PlanUnavailable,
    LedgerUnavailable,
    UnsafeCoordination,
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
///     A host without that capability must return no session.
/// </summary>
internal interface IJournaledRepositoryEffectBroker
{
    Task<IJournaledRepositoryEffectSession?> AcquireAsync(
        StoredChangePlan plan,
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
    ///     Returns no effect session. The default host therefore cannot make a
    ///     repository effect, even when an exact plan and reservation exist.
    /// </summary>
    public Task<IJournaledRepositoryEffectSession?> AcquireAsync(
        StoredChangePlan plan,
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

    internal JournaledApplyAuthority(
        DurableChangePlanStore planStore,
        DurableOperationLedger ledger,
        RepositoryMutationReservationProvider reservations,
        IJournaledRepositoryEffectBroker? broker = null)
    {
        _planStore = planStore;
        _ledger = ledger;
        _reservations = reservations;
        _broker = broker ?? UnavailableJournaledRepositoryEffectBroker.Instance;
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

            IJournaledRepositoryEffectSession? session;
            try
            {
                session = await _broker.AcquireAsync(plan, reservation, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                return Fail(JournaledApplyErrorKind.UnsafeCoordination,
                    "The required repository effect guard could not be acquired.", exception);
            }
            if (session is null)
            {
                return Fail(JournaledApplyErrorKind.UnsafeCoordination,
                    "No OS-enforced repository effect broker is available.");
            }

            await using (session)
            {
                var preflight = await session.FinalPreflightAsync(cancellationToken);
                if (preflight is null
                    || !preflight.GuardTokens.SequenceEqual(
                        OperationGuardTokenSet.Expected(plan.Envelope.Payload)))
                {
                    return Fail(JournaledApplyErrorKind.UnsafeCoordination,
                        "Final guarded preflight did not prove every plan dependency.");
                }

                var applying = _ledger.ClaimApplyOwner(
                    request.RepositoryId,
                    request.OperationId,
                    reservation,
                    request.Identity.PlanHash);
                if (applying.IsErr)
                {
                    var error = applying.Match(_ => null!, value => value);
                    return Fail(JournaledApplyErrorKind.LedgerUnavailable, error.Message);
                }

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

    private static Result<DurableOperationRecord, JournaledApplyError> Fail(
        JournaledApplyErrorKind kind,
        string message,
        Exception? cause = null) =>
        Result<DurableOperationRecord, JournaledApplyError>.Err(new(kind, message, cause));
}
