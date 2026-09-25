using System.Security.Cryptography;

namespace RoslynTools.Core;

internal sealed record DispatchPlanRegistration(
    CanonicalApplyRequest Request,
    AuthenticatedPrincipalId SubmittingPrincipal,
    string DispatchLeaseId,
    DateTimeOffset DispatchLeaseExpiresAtUtc,
    string IntakeAttemptId,
    DateTimeOffset IntakeAttemptExpiresAtUtc);

internal enum DurableDispatchIntakeDisposition
{
    Ready,
    Prepared,
    Replay,
    Conflict,
    AccessDenied,
    Indeterminate,
    RejectedPlanUnavailable
}

internal enum DurableDispatchIntakeErrorKind
{
    CoordinationUnavailable,
    UnsafeProtection,
    CorruptState
}

internal sealed record DurableDispatchIntakeError(
    DurableDispatchIntakeErrorKind Kind,
    string Message,
    IReadOnlyDictionary<string, string>? Evidence = null);

internal sealed record DurableDispatchIntakeResult(
    DurableDispatchIntakeDisposition Disposition,
    DurableDispatchIntakeRecord? Dispatch,
    DurableOperationRecord? Operation);

internal enum DispatchIntakeBoundary
{
    DispatchRecordCommitted,
    DispatchPlanPinned,
    DispatchReadyCommitted,
    IntakePlanPinned,
    IntakePinnedCommitted,
    ReceiptCommitted,
    ReceiptPhaseCommitted,
    OperationPlanPinned,
    OperationPinnedCommitted,
    PreparedLifecycleCommitted,
    PreparedPhaseCommitted
}

internal sealed record DurableDispatchIntakeHooks(
    Action<DispatchIntakeBoundary>? AfterBoundary = null,
    Action? AfterMissingOperationPinReadback = null);

/// <summary>
///     Reconciles the exact stored-plan pin and durable intake receipt into one
///     restart-safe Prepared handoff. It deliberately authorizes no repository effect.
/// </summary>
internal sealed class DurableDispatchIntakeCoordinator
{
    private readonly DurableChangePlanStore _planStore;
    private readonly DurableOperationLedger _ledger;
    private readonly DurableDispatchIntakeHooks? _hooks;

    internal DurableDispatchIntakeCoordinator(
        DurableChangePlanStore planStore,
        DurableOperationLedger ledger,
        DurableDispatchIntakeHooks? hooks = null)
    {
        _planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _hooks = hooks;
    }

    internal Result<DurableDispatchIntakeResult, DurableDispatchIntakeError> RegisterDispatchPlan(
        DispatchPlanRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(registration.Request);
        ArgumentNullException.ThrowIfNull(registration.SubmittingPrincipal);

        var registered = RegisterRequestIdentity(registration);
        if (registered.IsErr)
            return registered;
        var registrationResult = registered.Match(value => value, _ => null!);
        if (registrationResult.Disposition != DurableDispatchIntakeDisposition.Ready)
            return registered;

        var dispatch = registrationResult.Dispatch!;
        if (dispatch.CurrentPhase == DispatchIntakePhase.RejectedPlanUnavailable)
            return LoadRejected(dispatch);
        if (dispatch.CurrentPhase == DispatchIntakePhase.Prepared)
            return LoadPrepared(dispatch, DurableDispatchIntakeDisposition.Replay);

        var reconciled = ReconcileRegistration(dispatch);
        if (reconciled.IsErr)
            return reconciled.Map(value => new DurableDispatchIntakeResult(
                DurableDispatchIntakeDisposition.Ready,
                value,
                null));
        dispatch = reconciled.Match(value => value, _ => null!);
        return dispatch.CurrentPhase == DispatchIntakePhase.RejectedPlanUnavailable
            ? Outcome(DurableDispatchIntakeDisposition.RejectedPlanUnavailable, dispatch)
            : Outcome(DurableDispatchIntakeDisposition.Ready, dispatch);
    }

    /// <summary>
    ///     Reads the retained identity before any new plan-store access. Exact replay,
    ///     conflicting reuse, and cross-principal access denial can therefore be resolved
    ///     without publishing or probing caller-selected plan bytes.
    /// </summary>
    internal Result<DurableDispatchIntakeResult, DurableDispatchIntakeError> InspectRequestIdentity(
        CanonicalApplyRequest request,
        AuthenticatedPrincipalId submittingPrincipal)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(submittingPrincipal);

        var existing = _ledger.LoadDispatch(request.RepositoryId, request.OperationId);
        if (existing.IsErr)
        {
            var error = existing.Match(_ => null!, value => value);
            return error.Kind == OperationLedgerErrorKind.Missing
                ? Outcome(DurableDispatchIntakeDisposition.Ready)
                : LedgerError<DurableDispatchIntakeResult>(error);
        }

        var dispatch = existing.Match(value => value, _ => null!);
        if (!string.Equals(
                dispatch.SubmittingPrincipalId,
                submittingPrincipal.Value,
                StringComparison.Ordinal))
        {
            return Outcome(DurableDispatchIntakeDisposition.AccessDenied);
        }
        if (!SameRequest(dispatch, request))
            return Outcome(DurableDispatchIntakeDisposition.Conflict);
        if (dispatch.CurrentPhase == DispatchIntakePhase.RejectedPlanUnavailable)
            return LoadRejected(dispatch);
        if (dispatch.CurrentPhase == DispatchIntakePhase.Prepared)
            return LoadPrepared(dispatch, DurableDispatchIntakeDisposition.Replay);
        return Outcome(DurableDispatchIntakeDisposition.Ready, dispatch);
    }

    /// <summary>
    ///     Durably binds a new operation identity before the stored plan is opened. This
    ///     ordering prevents conflicts and access denials from changing plan-store state,
    ///     while an interrupted first attempt can resume from the retained registration.
    /// </summary>
    internal Result<DurableDispatchIntakeResult, DurableDispatchIntakeError> RegisterRequestIdentity(
        DispatchPlanRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(registration.Request);
        ArgumentNullException.ThrowIfNull(registration.SubmittingPrincipal);

        var inspected = InspectRequestIdentity(
            registration.Request,
            registration.SubmittingPrincipal);
        if (inspected.IsErr)
            return inspected;
        var inspection = inspected.Match(value => value, _ => null!);
        if (inspection.Disposition != DurableDispatchIntakeDisposition.Ready
            || inspection.Dispatch is not null)
        {
            return inspected;
        }

        _planStore.ValidateDispatchIntakeRegistration(
            registration.DispatchLeaseId,
            registration.DispatchLeaseExpiresAtUtc,
            registration.IntakeAttemptId,
            FormatOperationReference(registration.Request.OperationId));

        var registered = _ledger.RegisterDispatch(
            registration.Request,
            registration.SubmittingPrincipal,
            registration.DispatchLeaseId,
            registration.DispatchLeaseExpiresAtUtc,
            registration.IntakeAttemptId,
            registration.IntakeAttemptExpiresAtUtc);
        DispatchIntakeRegistrationResult registrationResult;
        if (registered.IsErr)
        {
            var registrationError = registered.Match(_ => null!, error => error);
            if (registrationError.Kind != OperationLedgerErrorKind.Unreadable)
                return LedgerError<DurableDispatchIntakeResult>(registrationError);
            var retried = _ledger.RegisterDispatch(
                registration.Request,
                registration.SubmittingPrincipal,
                registration.DispatchLeaseId,
                registration.DispatchLeaseExpiresAtUtc,
                registration.IntakeAttemptId,
                registration.IntakeAttemptExpiresAtUtc);
            if (retried.IsErr)
                return LedgerError<DurableDispatchIntakeResult>(retried.Match(_ => null!, error => error));
            registrationResult = retried.Match(value => value, _ => null!);
        }
        else
        {
            registrationResult = registered.Match(value => value, _ => null!);
        }
        if (registrationResult.Disposition == DispatchIntakeRegistrationDisposition.Registered)
            Reached(DispatchIntakeBoundary.DispatchRecordCommitted);
        return registrationResult.Disposition switch
        {
            DispatchIntakeRegistrationDisposition.Conflict =>
                Outcome(DurableDispatchIntakeDisposition.Conflict),
            DispatchIntakeRegistrationDisposition.AccessDenied =>
                Outcome(DurableDispatchIntakeDisposition.AccessDenied),
            _ => Outcome(DurableDispatchIntakeDisposition.Ready, registrationResult.Dispatch)
        };
    }

    internal Result<DurableDispatchIntakeResult, DurableDispatchIntakeError> ReceiveAndPrepare(
        CanonicalApplyRequest request,
        AuthenticatedPrincipalId submittingPrincipal)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(submittingPrincipal);

        var loaded = _ledger.LoadDispatch(request.RepositoryId, request.OperationId);
        if (loaded.IsErr)
        {
            var error = loaded.Match(_ => null!, value => value);
            return error.Kind == OperationLedgerErrorKind.Missing
                ? Outcome(DurableDispatchIntakeDisposition.Indeterminate)
                : LedgerError<DurableDispatchIntakeResult>(error);
        }

        var dispatch = loaded.Match(value => value, _ => null!);
        if (!string.Equals(
                dispatch.SubmittingPrincipalId,
                submittingPrincipal.Value,
                StringComparison.Ordinal))
        {
            return Outcome(DurableDispatchIntakeDisposition.AccessDenied);
        }
        if (!SameRequest(dispatch, request))
            return Outcome(DurableDispatchIntakeDisposition.Conflict);
        if (dispatch.CurrentPhase == DispatchIntakePhase.RejectedPlanUnavailable)
            return LoadRejected(dispatch);
        if (dispatch.CurrentPhase == DispatchIntakePhase.Prepared)
            return LoadPrepared(dispatch, DurableDispatchIntakeDisposition.Replay);

        var acceptedReceipt = false;
        while (true)
        {
            switch (dispatch.CurrentPhase)
            {
                case DispatchIntakePhase.Registered:
                    {
                        var registration = ReconcileRegistration(dispatch);
                        if (registration.IsErr)
                        {
                            return registration.Map(value => new DurableDispatchIntakeResult(
                                DurableDispatchIntakeDisposition.Ready,
                                value,
                                null));
                        }
                        dispatch = registration.Match(value => value, _ => null!);
                        break;
                    }
                case DispatchIntakePhase.DispatchPinned:
                    {
                        var pin = BeginIntakePin(dispatch);
                        if (pin.IsErr)
                        {
                            var planError = pin.Match(_ => null!, error => error);
                            if (planError.Kind == ChangePlanStoreErrorKind.UnsafeProtection)
                                return PlanStoreError<DurableDispatchIntakeResult>(planError);
                            var observedPin = ObserveExpectedPin(
                                dispatch,
                                ChangePlanPinKind.IntakePending,
                                dispatch.IntakeAttemptId);
                            if (observedPin.IsErr)
                            {
                                return observedPin.Map(value => new DurableDispatchIntakeResult(
                                    DurableDispatchIntakeDisposition.Indeterminate,
                                    dispatch,
                                    null));
                            }
                            if (observedPin.Match(
                                    value => value,
                                    // Stryker disable once Boolean : The preceding IsErr guard makes this fallback unreachable.
                                    _ => false))
                                goto IntakePinConfirmed;
                            var current = ReloadDispatch(dispatch);
                            if (current.IsErr)
                            {
                                return current.Map(value => new DurableDispatchIntakeResult(
                                    DurableDispatchIntakeDisposition.Indeterminate,
                                    value,
                                    null));
                            }
                            var observed = current.Match(value => value, _ => null!);
                            if (observed.CurrentPhase != DispatchIntakePhase.DispatchPinned)
                            {
                                dispatch = observed;
                                break;
                            }
                            return RejectAfterReceipt(dispatch, request, submittingPrincipal, planError);
                        }
                    IntakePinConfirmed:
                        Reached(DispatchIntakeBoundary.IntakePlanPinned);
                        var advanced = AdvanceOrReload(
                            dispatch,
                            DispatchIntakePhase.IntakePinned,
                            "The exact dispatch-plan lease is retained by the intake attempt.");
                        if (advanced.IsErr)
                            return advanced.Map(value => new DurableDispatchIntakeResult(
                                DurableDispatchIntakeDisposition.Indeterminate,
                                value,
                                null));
                        dispatch = advanced.Match(value => value, _ => null!);
                        Reached(DispatchIntakeBoundary.IntakePinnedCommitted);
                        break;
                    }
                case DispatchIntakePhase.IntakePinned:
                    {
                        var receipt = ReceiveOrReload(request, submittingPrincipal);
                        if (receipt.IsErr)
                            return LedgerError<DurableDispatchIntakeResult>(receipt.Match(_ => null!, error => error));
                        var intake = receipt.Match(value => value, _ => null!);
                        if (intake.Disposition == OperationIntakeDisposition.AccessDenied)
                            return Outcome(DurableDispatchIntakeDisposition.AccessDenied);
                        if (intake.Disposition == OperationIntakeDisposition.Conflict)
                            return Outcome(DurableDispatchIntakeDisposition.Conflict);
                        // Stryker disable once Assignment : A canonical operation can be accepted only once; later exact receives are Replay, so OR and XOR are equivalent here.
                        acceptedReceipt |= intake.Disposition == OperationIntakeDisposition.Accepted;
                        Reached(DispatchIntakeBoundary.ReceiptCommitted);
                        var advanced = AdvanceOrReload(
                            dispatch,
                            DispatchIntakePhase.Received,
                            "The durable operation receipt was committed.");
                        if (advanced.IsErr)
                            return advanced.Map(value => new DurableDispatchIntakeResult(
                                DurableDispatchIntakeDisposition.Indeterminate,
                                value,
                                intake.Operation));
                        dispatch = advanced.Match(value => value, _ => null!);
                        Reached(DispatchIntakeBoundary.ReceiptPhaseCommitted);
                        break;
                    }
                case DispatchIntakePhase.Received:
                    {
                        var receipt = LoadOperationAtPreparationFrontier(dispatch);
                        if (receipt.IsErr)
                        {
                            return receipt.Map(operation => new DurableDispatchIntakeResult(
                                DurableDispatchIntakeDisposition.Indeterminate,
                                dispatch,
                                operation));
                        }
                        var operationReference = FormatOperationReference(dispatch.OperationId);
                        var promoted = PromoteIntakePin(dispatch, operationReference);
                        if (promoted.IsErr)
                        {
                            var planError = promoted.Match(_ => null!, error => error);
                            if (planError.Kind == ChangePlanStoreErrorKind.UnsafeProtection)
                                return PlanStoreError<DurableDispatchIntakeResult>(planError);
                            var observedPin = ObserveExpectedPin(
                                dispatch,
                                ChangePlanPinKind.OperationLedger,
                                operationReference);
                            if (observedPin.IsErr)
                            {
                                return observedPin.Map(value => new DurableDispatchIntakeResult(
                                    DurableDispatchIntakeDisposition.Indeterminate,
                                    dispatch,
                                    null));
                            }
                            if (observedPin.Match(
                                    value => value,
                                    // Stryker disable once Boolean : The preceding IsErr guard makes this fallback unreachable.
                                    _ => false))
                                goto OperationPinConfirmed;
                            _hooks?.AfterMissingOperationPinReadback?.Invoke();
                            var current = ReloadDispatch(dispatch);
                            if (current.IsErr)
                            {
                                return current.Map(value => new DurableDispatchIntakeResult(
                                    DurableDispatchIntakeDisposition.Indeterminate,
                                    value,
                                    null));
                            }
                            var observed = current.Match(value => value, _ => null!);
                            if (observed.CurrentPhase != DispatchIntakePhase.Received)
                            {
                                dispatch = observed;
                                break;
                            }
                            return RejectExistingReceipt(dispatch, planError);
                        }
                    OperationPinConfirmed:
                        Reached(DispatchIntakeBoundary.OperationPlanPinned);
                        var advanced = AdvanceOrReload(
                            dispatch,
                            DispatchIntakePhase.OperationPinned,
                            "The intake pin was promoted to the durable operation ledger.");
                        if (advanced.IsErr)
                            return advanced.Map(value => new DurableDispatchIntakeResult(
                                DurableDispatchIntakeDisposition.Indeterminate,
                                value,
                                null));
                        dispatch = advanced.Match(value => value, _ => null!);
                        Reached(DispatchIntakeBoundary.OperationPinnedCommitted);
                        break;
                    }
                case DispatchIntakePhase.OperationPinned:
                    {
                        var frontier = LoadOperationAtPreparationFrontier(dispatch);
                        if (frontier.IsErr)
                        {
                            return frontier.Map(operation => new DurableDispatchIntakeResult(
                                DurableDispatchIntakeDisposition.Indeterminate,
                                dispatch,
                                operation));
                        }
                        var operation = frontier.Match(value => value, _ => null!);
                        if (operation.CurrentState == OperationLifecycleState.Received)
                        {
                            var prepared = _ledger.AppendTransition(
                                dispatch.RepositoryId,
                                dispatch.OperationId,
                                OperationLifecycleState.Received,
                                OperationLifecycleState.Prepared,
                                "The exact stored plan was verified and pinned for later effect admission.");
                            if (prepared.IsErr)
                            {
                                var ledgerError = prepared.Match(_ => null!, error => error);
                                if (IsStrongSafetyError(ledgerError))
                                    return LedgerError<DurableDispatchIntakeResult>(ledgerError);
                                var current = LoadOperationAtPreparationFrontier(dispatch);
                                if (current.IsErr)
                                {
                                    return current.Map(value => new DurableDispatchIntakeResult(
                                        DurableDispatchIntakeDisposition.Indeterminate,
                                        dispatch,
                                        value));
                                }
                                operation = current.Match(value => value, _ => null!);
                                if (operation.CurrentState == OperationLifecycleState.Received)
                                    return LedgerError<DurableDispatchIntakeResult>(ledgerError);
                            }
                            else
                            {
                                operation = prepared.Match(value => value, _ => null!);
                            }
                        }
                        Reached(DispatchIntakeBoundary.PreparedLifecycleCommitted);

                        var advanced = AdvanceOrReload(
                            dispatch,
                            DispatchIntakePhase.Prepared,
                            "The no-effect Prepared handoff was committed.");
                        if (advanced.IsErr)
                            return advanced.Map(value => new DurableDispatchIntakeResult(
                                DurableDispatchIntakeDisposition.Indeterminate,
                                value,
                                operation));
                        dispatch = advanced.Match(value => value, _ => null!);
                        Reached(DispatchIntakeBoundary.PreparedPhaseCommitted);
                        return Outcome(
                            acceptedReceipt
                                ? DurableDispatchIntakeDisposition.Prepared
                                : DurableDispatchIntakeDisposition.Replay,
                            dispatch,
                            operation);
                    }
                case DispatchIntakePhase.Prepared:
                    return LoadPrepared(
                        dispatch,
                        DurableDispatchIntakeDisposition.Replay);
                case DispatchIntakePhase.RejectedPlanUnavailable:
                    return LoadRejected(dispatch);
                default:
                    // Stryker disable once String : The typed corruption result is the contract; wording is diagnostic.
                    return Corrupt<DurableDispatchIntakeResult>("The dispatch intake phase is unsupported.");
            }
        }
    }

    internal static string FormatOperationReference(OperationId operationId)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        return $"{operationId.Epoch}:{operationId.Nonce:N}";
    }

    private Result<OperationIntakeResult, OperationLedgerError> ReceiveOrReload(
        CanonicalApplyRequest request,
        AuthenticatedPrincipalId principal)
    {
        var received = _ledger.Receive(request, principal);
        if (received.IsOk)
            return received;
        var originalError = received.Match(_ => null!, error => error);
        if (originalError.Kind != OperationLedgerErrorKind.Unreadable)
            return received;
        return _ledger.Receive(request, principal);
    }

    private Result<StoredChangePlan, ChangePlanStoreError> PinDispatchLease(
        DurableDispatchIntakeRecord dispatch,
        ChangePlanStoreKey key)
    {
        try
        {
            return _planStore.PinDispatchLease(
                key,
                dispatch.DispatchLeaseId,
                dispatch.DispatchLeaseExpiresAtUtc);
        }
        catch (ArgumentException exception)
        {
            return Result<StoredChangePlan, ChangePlanStoreError>.Err(new ChangePlanStoreError(
                ChangePlanStoreErrorKind.Conflict,
                // Stryker disable once String : The typed conflict is the contract; exception wording remains diagnostic.
                $"The durable dispatch lease is invalid: {exception.Message}"));
        }
    }

    private Result<StoredChangePlan, ChangePlanStoreError> BeginIntakePin(
        DurableDispatchIntakeRecord dispatch)
    {
        try
        {
            return _planStore.BeginIntakePin(
                Key(dispatch),
                dispatch.DispatchLeaseId,
                dispatch.IntakeAttemptId);
        }
        catch (ArgumentException exception)
        {
            return Result<StoredChangePlan, ChangePlanStoreError>.Err(new ChangePlanStoreError(
                ChangePlanStoreErrorKind.Conflict,
                // Stryker disable once String : The typed conflict is the contract; exception wording is diagnostic.
                $"The durable intake pin is invalid: {exception.Message}"));
        }
    }

    private Result<StoredChangePlan, ChangePlanStoreError> PromoteIntakePin(
        DurableDispatchIntakeRecord dispatch,
        string operationReference)
    {
        try
        {
            return _planStore.PromoteIntakePin(
                Key(dispatch),
                dispatch.DispatchLeaseId,
                dispatch.IntakeAttemptId,
                operationReference);
        }
        catch (ArgumentException exception)
        {
            return Result<StoredChangePlan, ChangePlanStoreError>.Err(new ChangePlanStoreError(
                ChangePlanStoreErrorKind.Conflict,
                // Stryker disable once String : The typed conflict is the contract; exception wording is diagnostic.
                $"The durable operation pin is invalid: {exception.Message}"));
        }
    }

    private Result<bool, DurableDispatchIntakeError> ObserveExpectedPin(
        DurableDispatchIntakeRecord dispatch,
        ChangePlanPinKind kind,
        string referenceId)
    {
        var loaded = _planStore.LoadRetained(Key(dispatch));
        if (loaded.IsErr)
        {
            var error = loaded.Match(_ => null!, value => value);
            return error.Kind == ChangePlanStoreErrorKind.Missing
                ? Result<bool, DurableDispatchIntakeError>.Ok(false)
                : PlanStoreError<bool>(error);
        }
        var pins = loaded.Match(value => value.Pins, _ => []);
        var candidate = pins.SingleOrDefault(pin =>
            pin.Kind == kind
            && string.Equals(pin.ReferenceId, referenceId, StringComparison.Ordinal));
        if (candidate is null)
            return Result<bool, DurableDispatchIntakeError>.Ok(false);
        var exact = kind switch
        {
            ChangePlanPinKind.DispatchLease =>
                candidate.ExpiresAtUtc == dispatch.DispatchLeaseExpiresAtUtc
                && candidate.AdoptedFromDispatchLeaseId is null,
            ChangePlanPinKind.IntakePending or ChangePlanPinKind.OperationLedger =>
                candidate.ExpiresAtUtc is null
                && string.Equals(
                    candidate.AdoptedFromDispatchLeaseId,
                    dispatch.DispatchLeaseId,
                    StringComparison.Ordinal),
            // Stryker disable once Boolean : Private callers pass only the three declared pin kinds handled above.
            _ => false
        };
        return exact
            ? Result<bool, DurableDispatchIntakeError>.Ok(true)
            : Corrupt<bool>(
                // Stryker disable once String : The typed corruption boundary is the contract; wording is diagnostic.
                "The retained plan pin contradicts the durable dispatch binding.");
    }

    private Result<DurableDispatchIntakeRecord, DurableDispatchIntakeError> ReconcileRegistration(
        DurableDispatchIntakeRecord dispatch)
    {
        // Stryker disable once Equality : Inverting this phase guard creates a nonterminating Registered reconciliation mutant.
        if (dispatch.CurrentPhase != DispatchIntakePhase.Registered)
            return Result<DurableDispatchIntakeRecord, DurableDispatchIntakeError>.Ok(dispatch);

        var loadedPlan = _planStore.LoadRetained(Key(dispatch));
        if (loadedPlan.IsErr)
        {
            var error = loadedPlan.Match(_ => null!, value => value);
            if (error.Kind == ChangePlanStoreErrorKind.UnsafeProtection)
                return PlanStoreError<DurableDispatchIntakeRecord>(error);
            return RejectBeforeDispatch(dispatch, error);
        }
        var plan = loadedPlan.Match(value => value, _ => null!);
        if (!PlanMatchesRequest(plan, dispatch))
        {
            return RejectBeforeDispatch(
                dispatch,
                new ChangePlanStoreError(
                    ChangePlanStoreErrorKind.Conflict,
                    // Stryker disable once String : The typed plan conflict is the contract; wording is diagnostic.
                    "The persisted plan does not match the canonical apply request."));
        }

        var pinned = PinDispatchLease(dispatch, plan.Key);
        if (pinned.IsErr)
        {
            var error = pinned.Match(_ => null!, value => value);
            if (error.Kind == ChangePlanStoreErrorKind.UnsafeProtection)
                return PlanStoreError<DurableDispatchIntakeRecord>(error);
            var observedPin = ObserveExpectedPin(
                dispatch,
                ChangePlanPinKind.DispatchLease,
                dispatch.DispatchLeaseId);
            if (observedPin.IsErr)
                return observedPin.Map(_ => dispatch);
            if (observedPin.Match(
                    value => value,
                    // Stryker disable once Boolean : The preceding IsErr guard makes this fallback unreachable.
                    _ => false))
                goto DispatchPinConfirmed;
            return RejectBeforeDispatch(dispatch, error);
        }
    DispatchPinConfirmed:
        Reached(DispatchIntakeBoundary.DispatchPlanPinned);

        var advanced = AdvanceOrReload(
            dispatch,
            DispatchIntakePhase.DispatchPinned,
            "The exact stored plan is pinned before dispatch.");
        if (advanced.IsOk)
            Reached(DispatchIntakeBoundary.DispatchReadyCommitted);
        return advanced;
    }

    private Result<DurableDispatchIntakeResult, DurableDispatchIntakeError> RejectAfterReceipt(
        DurableDispatchIntakeRecord dispatch,
        CanonicalApplyRequest request,
        AuthenticatedPrincipalId principal,
        ChangePlanStoreError planError)
    {
        var receipt = ReceiveOrReload(request, principal);
        if (receipt.IsErr)
            return LedgerError<DurableDispatchIntakeResult>(receipt.Match(_ => null!, error => error));
        var intake = receipt.Match(value => value, _ => null!);
        if (intake.Disposition == OperationIntakeDisposition.AccessDenied)
            return Outcome(DurableDispatchIntakeDisposition.AccessDenied);
        if (intake.Disposition == OperationIntakeDisposition.Conflict)
            return Outcome(DurableDispatchIntakeDisposition.Conflict);
        Reached(DispatchIntakeBoundary.ReceiptCommitted);
        return RejectExistingReceipt(dispatch, planError);
    }

    private Result<DurableDispatchIntakeResult, DurableDispatchIntakeError> RejectExistingReceipt(
        DurableDispatchIntakeRecord dispatch,
        ChangePlanStoreError planError)
    {
        var current = _ledger.Load(dispatch.RepositoryId, dispatch.OperationId);
        if (current.IsErr)
        {
            var error = current.Match(_ => null!, value => value);
            return error.Kind == OperationLedgerErrorKind.Missing
                ? Corrupt<DurableDispatchIntakeResult>(
                    // Stryker disable once String : The typed cross-record corruption is the contract; wording is diagnostic.
                    "The rejection frontier has no operation receipt.")
                : LedgerError<DurableDispatchIntakeResult>(error);
        }
        var operation = current.Match(value => value, _ => null!);
        if (!OperationMatchesDispatch(operation, dispatch))
        {
            return Corrupt<DurableDispatchIntakeResult>(
                // Stryker disable once String : The typed cross-record corruption is the contract; wording is diagnostic.
                "The rejection receipt does not match its durable dispatch binding.");
        }
        if (operation.CurrentState == OperationLifecycleState.Received)
        {
            var rejected = _ledger.AppendTransition(
                dispatch.RepositoryId,
                dispatch.OperationId,
                OperationLifecycleState.Received,
                OperationLifecycleState.RejectedPlanUnavailable,
                $"Stored plan unavailable: {planError.Kind}.");
            if (rejected.IsErr)
            {
                var rejectionError = rejected.Match(_ => null!, error => error);
                if (IsStrongSafetyError(rejectionError))
                    return LedgerError<DurableDispatchIntakeResult>(rejectionError);
                var reloaded = _ledger.Load(dispatch.RepositoryId, dispatch.OperationId);
                if (reloaded.IsErr)
                {
                    var reloadError = reloaded.Match(_ => null!, error => error);
                    return reloadError.Kind == OperationLedgerErrorKind.Missing
                        ? Corrupt<DurableDispatchIntakeResult>(
                            // Stryker disable once String : The typed cross-record corruption is the contract; wording is diagnostic.
                            "The rejection frontier lost its operation receipt.")
                        : LedgerError<DurableDispatchIntakeResult>(reloadError);
                }
                operation = reloaded.Match(value => value, _ => null!);
                if (!OperationMatchesDispatch(operation, dispatch))
                {
                    return Corrupt<DurableDispatchIntakeResult>(
                        // Stryker disable once String : The typed cross-record corruption is the contract; wording is diagnostic.
                        "The reloaded rejection receipt does not match its durable dispatch binding.");
                }
                if (operation.CurrentState == OperationLifecycleState.Received)
                    return LedgerError<DurableDispatchIntakeResult>(rejectionError);
                if (operation.CurrentState != OperationLifecycleState.RejectedPlanUnavailable)
                {
                    return Corrupt<DurableDispatchIntakeResult>(
                        // Stryker disable once String : The typed corruption result is the contract; wording is diagnostic.
                        "The operation left the expected rejection frontier.");
                }
            }
            else
            {
                operation = rejected.Match(value => value, _ => null!);
            }
        }
        else if (operation.CurrentState != OperationLifecycleState.RejectedPlanUnavailable)
        {
            return Corrupt<DurableDispatchIntakeResult>(
                // Stryker disable once String : The typed corruption result is the contract; wording is diagnostic.
                "The operation cannot be rejected from its current lifecycle state.");
        }

        var advanced = AdvanceToRejected(dispatch, planError);
        if (advanced.IsErr)
        {
            return advanced.Map(value => new DurableDispatchIntakeResult(
                DurableDispatchIntakeDisposition.RejectedPlanUnavailable,
                value,
                operation));
        }
        return Outcome(
            DurableDispatchIntakeDisposition.RejectedPlanUnavailable,
            advanced.Match(value => value, _ => null!),
            operation);
    }

    private Result<DurableDispatchIntakeRecord, DurableDispatchIntakeError> RejectBeforeDispatch(
        DurableDispatchIntakeRecord dispatch,
        ChangePlanStoreError planError) => AdvanceToRejected(dispatch, planError);

    private Result<DurableDispatchIntakeRecord, DurableDispatchIntakeError> AdvanceToRejected(
        DurableDispatchIntakeRecord dispatch,
        ChangePlanStoreError planError)
    {
        if (dispatch.CurrentPhase == DispatchIntakePhase.RejectedPlanUnavailable)
            return Result<DurableDispatchIntakeRecord, DurableDispatchIntakeError>.Ok(dispatch);
        var advanced = _ledger.AdvanceDispatch(
            dispatch.RepositoryId,
            dispatch.OperationId,
            dispatch.CurrentPhase,
            DispatchIntakePhase.RejectedPlanUnavailable,
            $"Stored plan unavailable: {planError.Kind}.");
        if (advanced.IsErr)
        {
            var error = advanced.Match(_ => null!, value => value);
            if (IsStrongSafetyError(error))
                return LedgerError<DurableDispatchIntakeRecord>(error);
            var reloaded = _ledger.LoadDispatch(dispatch.RepositoryId, dispatch.OperationId);
            if (reloaded.IsErr)
                return LedgerError<DurableDispatchIntakeRecord>(reloaded.Match(_ => null!, value => value));
            var current = reloaded.Match(value => value, _ => null!);
            if (current.CurrentPhase == DispatchIntakePhase.RejectedPlanUnavailable)
                return Result<DurableDispatchIntakeRecord, DurableDispatchIntakeError>.Ok(current);
            return current.CurrentPhase == dispatch.CurrentPhase
                ? LedgerError<DurableDispatchIntakeRecord>(error)
                : Corrupt<DurableDispatchIntakeRecord>(
                    // Stryker disable once String : The typed corruption result is the contract; wording is diagnostic.
                    "The dispatch intake left the rejection frontier.");
        }
        return Result<DurableDispatchIntakeRecord, DurableDispatchIntakeError>.Ok(
            advanced.Match(value => value, _ => null!));
    }

    private Result<DurableDispatchIntakeRecord, DurableDispatchIntakeError> AdvanceOrReload(
        DurableDispatchIntakeRecord dispatch,
        DispatchIntakePhase nextPhase,
        string evidence)
    {
        var advanced = _ledger.AdvanceDispatch(
            dispatch.RepositoryId,
            dispatch.OperationId,
            dispatch.CurrentPhase,
            nextPhase,
            evidence);
        // Stryker disable once Block : Removing this block performs a redundant read-back and returns the same committed phase.
        if (advanced.IsOk)
        {
            return Result<DurableDispatchIntakeRecord, DurableDispatchIntakeError>.Ok(
                advanced.Match(value => value, _ => null!));
        }

        var error = advanced.Match(_ => null!, value => value);
        if (IsStrongSafetyError(error))
            return LedgerError<DurableDispatchIntakeRecord>(error);
        var reloaded = _ledger.LoadDispatch(dispatch.RepositoryId, dispatch.OperationId);
        if (reloaded.IsErr)
            return LedgerError<DurableDispatchIntakeRecord>(reloaded.Match(_ => null!, value => value));
        var current = reloaded.Match(value => value, _ => null!);
        return current.CurrentPhase == dispatch.CurrentPhase
            ? LedgerError<DurableDispatchIntakeRecord>(error)
            : Result<DurableDispatchIntakeRecord, DurableDispatchIntakeError>.Ok(current);
    }

    private Result<DurableDispatchIntakeRecord, DurableDispatchIntakeError> ReloadDispatch(
        DurableDispatchIntakeRecord dispatch)
    {
        var reloaded = _ledger.LoadDispatch(dispatch.RepositoryId, dispatch.OperationId);
        return reloaded.Match(
            value => Result<DurableDispatchIntakeRecord, DurableDispatchIntakeError>.Ok(value),
            LedgerError<DurableDispatchIntakeRecord>);
    }

    private Result<DurableOperationRecord, DurableDispatchIntakeError> LoadOperationAtPreparationFrontier(
        DurableDispatchIntakeRecord dispatch)
    {
        var loaded = _ledger.Load(dispatch.RepositoryId, dispatch.OperationId);
        if (loaded.IsErr)
        {
            var error = loaded.Match(_ => null!, value => value);
            return error.Kind == OperationLedgerErrorKind.Missing
                ? Corrupt<DurableOperationRecord>(
                    // Stryker disable once String : The typed cross-record corruption is the contract; wording is diagnostic.
                    "The durable dispatch frontier has no operation receipt.")
                : LedgerError<DurableOperationRecord>(error);
        }
        var operation = loaded.Match(value => value, _ => null!);
        if (!OperationMatchesDispatch(operation, dispatch))
        {
            return Corrupt<DurableOperationRecord>(
                // Stryker disable once String : The typed cross-record corruption is the contract; wording is diagnostic.
                "The operation receipt does not match its durable dispatch binding.");
        }
        return operation.CurrentState is OperationLifecycleState.Received or OperationLifecycleState.Prepared
            ? Result<DurableOperationRecord, DurableDispatchIntakeError>.Ok(operation)
            : Corrupt<DurableOperationRecord>(
                // Stryker disable once String : The typed cross-record corruption is the contract; wording is diagnostic.
                "The durable dispatch frontier contradicts the operation lifecycle.");
    }

    private Result<DurableDispatchIntakeResult, DurableDispatchIntakeError> LoadPrepared(
        DurableDispatchIntakeRecord dispatch,
        DurableDispatchIntakeDisposition disposition)
    {
        var operation = LoadOperationAtPreparationFrontier(dispatch);
        if (operation.IsErr)
        {
            return operation.Map(value => new DurableDispatchIntakeResult(
                disposition,
                dispatch,
                value));
        }
        var value = operation.Match(result => result, _ => null!);
        return value.CurrentState == OperationLifecycleState.Prepared
            ? Outcome(disposition, dispatch, value)
            : Corrupt<DurableDispatchIntakeResult>(
                // Stryker disable once String : The typed corruption result is the contract; wording is diagnostic.
                "Prepared dispatch evidence has no Prepared operation record.");
    }

    private Result<DurableDispatchIntakeResult, DurableDispatchIntakeError> LoadRejected(
        DurableDispatchIntakeRecord dispatch)
    {
        var operation = _ledger.Load(dispatch.RepositoryId, dispatch.OperationId);
        if (operation.IsErr)
        {
            var error = operation.Match(_ => null!, value => value);
            if (error.Kind == OperationLedgerErrorKind.Missing)
            {
                return DispatchHistoryRequiresReceipt(dispatch)
                    ? Corrupt<DurableDispatchIntakeResult>(
                        // Stryker disable once String : The typed cross-record corruption is the contract; wording is diagnostic.
                        "The rejected dispatch history has no required operation receipt.")
                    : Outcome(DurableDispatchIntakeDisposition.RejectedPlanUnavailable, dispatch);
            }
            return LedgerError<DurableDispatchIntakeResult>(error);
        }
        var value = operation.Match(result => result, _ => null!);
        if (!OperationMatchesDispatch(value, dispatch))
        {
            return Corrupt<DurableDispatchIntakeResult>(
                // Stryker disable once String : The typed cross-record corruption is the contract; wording is diagnostic.
                "The rejected operation does not match its durable dispatch binding.");
        }
        return value.CurrentState == OperationLifecycleState.RejectedPlanUnavailable
            ? Outcome(DurableDispatchIntakeDisposition.RejectedPlanUnavailable, dispatch, value)
            : Corrupt<DurableDispatchIntakeResult>(
                // Stryker disable once String : The typed corruption result is the contract; wording is diagnostic.
                "Rejected dispatch evidence has a contradictory operation state.");
    }

    private static bool DispatchHistoryRequiresReceipt(DurableDispatchIntakeRecord dispatch) =>
        dispatch.Transitions.Any(transition => transition.Phase == DispatchIntakePhase.Received)
        || dispatch.Transitions[^2].Phase == DispatchIntakePhase.DispatchPinned;

    private static bool IsStrongSafetyError(OperationLedgerError error) =>
        error.Kind is OperationLedgerErrorKind.Corrupt or OperationLedgerErrorKind.UnsafeProtection;

    private static bool OperationMatchesDispatch(
        DurableOperationRecord operation,
        DurableDispatchIntakeRecord dispatch) =>
        // Stryker disable once Logical : Validated canonical bytes and their parsed request identity covary; both checks retain collision defense.
        operation.RequestIdentity == dispatch.RequestIdentity
        && CryptographicOperations.FixedTimeEquals(
            operation.CanonicalRequestBytes.AsSpan(),
            dispatch.CanonicalRequestBytes.AsSpan())
        && string.Equals(
            operation.SubmittingPrincipalId,
            dispatch.SubmittingPrincipalId,
            StringComparison.Ordinal);

    private static ChangePlanStoreKey Key(DurableDispatchIntakeRecord dispatch) => new(
        dispatch.RepositoryId,
        dispatch.RequestIdentity.PlanId,
        dispatch.RequestIdentity.PlanHash);

    private static bool PlanMatchesRequest(
        StoredChangePlan plan,
        DurableDispatchIntakeRecord dispatch)
    {
        // Stryker disable all : LoadRetained used this exact complete key and reverified the envelope; these checks remain collision-defense assertions.
        if (!string.Equals(plan.Key.RepositoryId, dispatch.RepositoryId, StringComparison.Ordinal)
            || plan.Key.PlanId != dispatch.RequestIdentity.PlanId
            || !FixedTimeHashEquals(plan.Key.PlanHash, dispatch.RequestIdentity.PlanHash))
        {
            return false;
        }
        // Stryker restore all
        return string.Equals(
            plan.Envelope.Payload.Repository.BaseRevision,
            dispatch.RequestIdentity.ExpectedRevision,
            StringComparison.Ordinal);
    }

    // Stryker disable once Logical : Canonical bytes and their parsed request identity covary; both checks remain as collision defense.
    private static bool SameRequest(
        DurableDispatchIntakeRecord dispatch,
        CanonicalApplyRequest request) =>
        dispatch.RequestIdentity == request.Identity
        && CryptographicOperations.FixedTimeEquals(
            dispatch.CanonicalRequestBytes.AsSpan(),
            request.CanonicalBytes.AsSpan());

    // Stryker disable all : This helper is used only by the collision-defense assertions above.
    private static bool FixedTimeHashEquals(string left, string right)
    {
        var leftBytes = System.Text.Encoding.ASCII.GetBytes(left);
        var rightBytes = System.Text.Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
    // Stryker restore all

    private static Result<T, DurableDispatchIntakeError> LedgerError<T>(OperationLedgerError error) =>
        Result<T, DurableDispatchIntakeError>.Err(new DurableDispatchIntakeError(
            error.Kind == OperationLedgerErrorKind.UnsafeProtection
                ? DurableDispatchIntakeErrorKind.UnsafeProtection
                : error.Kind == OperationLedgerErrorKind.Corrupt
                    ? DurableDispatchIntakeErrorKind.CorruptState
                    : DurableDispatchIntakeErrorKind.CoordinationUnavailable,
            error.Message,
            error.Evidence));

    private static Result<T, DurableDispatchIntakeError> PlanStoreError<T>(ChangePlanStoreError error) =>
        Result<T, DurableDispatchIntakeError>.Err(new DurableDispatchIntakeError(
            error.Kind == ChangePlanStoreErrorKind.UnsafeProtection
                ? DurableDispatchIntakeErrorKind.UnsafeProtection
                : DurableDispatchIntakeErrorKind.CoordinationUnavailable,
            error.Message,
            error.Evidence));

    private static Result<T, DurableDispatchIntakeError> Corrupt<T>(string message) =>
        Result<T, DurableDispatchIntakeError>.Err(new DurableDispatchIntakeError(
            DurableDispatchIntakeErrorKind.CorruptState,
            message));

    private static Result<DurableDispatchIntakeResult, DurableDispatchIntakeError> Outcome(
        DurableDispatchIntakeDisposition disposition,
        DurableDispatchIntakeRecord? dispatch = null,
        DurableOperationRecord? operation = null) =>
        Result<DurableDispatchIntakeResult, DurableDispatchIntakeError>.Ok(
            new DurableDispatchIntakeResult(disposition, dispatch, operation));

    private void Reached(DispatchIntakeBoundary boundary) => _hooks?.AfterBoundary?.Invoke(boundary);
}
