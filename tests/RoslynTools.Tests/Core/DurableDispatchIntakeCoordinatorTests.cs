using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class DurableDispatchIntakeCoordinatorTests
{
    private static readonly DateTimeOffset InitialNow = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void coordinator_and_registration_boundaries_reject_missing_or_expired_inputs()
    {
        using var fixture = new CoordinatorFixture();
        Should.Throw<ArgumentNullException>(() => new DurableDispatchIntakeCoordinator(null!, fixture.Ledger))
            .ParamName.ShouldBe("planStore");
        Should.Throw<ArgumentNullException>(() => new DurableDispatchIntakeCoordinator(fixture.PlanStore, null!))
            .ParamName.ShouldBe("ledger");
        Should.Throw<ArgumentNullException>(() => fixture.Coordinator.RegisterDispatchPlan(null!))
            .ParamName.ShouldBe("registration");
        var invalidRegistration = fixture.CreateRegistration(
            fixture.CreateRequest(fixture.CreateEnvelope()));
        Should.Throw<ArgumentNullException>(() => fixture.Coordinator.RegisterDispatchPlan(
                invalidRegistration with { Request = null! }))
            .ParamName.ShouldBe("registration.Request");
        Should.Throw<ArgumentNullException>(() => fixture.Coordinator.RegisterDispatchPlan(
                invalidRegistration with { SubmittingPrincipal = null! }))
            .ParamName.ShouldBe("registration.SubmittingPrincipal");
        Should.Throw<ArgumentNullException>(() => fixture.Coordinator.ReceiveAndPrepare(null!, fixture.Principal))
            .ParamName.ShouldBe("request");
        Should.Throw<ArgumentNullException>(() => fixture.Coordinator.ReceiveAndPrepare(
                fixture.CreateRequest(fixture.CreateEnvelope()),
                null!))
            .ParamName.ShouldBe("submittingPrincipal");
        Should.Throw<ArgumentNullException>(() => DurableDispatchIntakeCoordinator.FormatOperationReference(null!))
            .ParamName.ShouldBe("operationId");
        var request = fixture.CreateRequest(fixture.CreateEnvelope());
        Should.Throw<ArgumentNullException>(() => fixture.Ledger.RegisterDispatch(
                null!,
                fixture.Principal,
                "dispatch",
                InitialNow + TimeSpan.FromMinutes(2),
                "attempt",
                InitialNow + TimeSpan.FromMinutes(1)))
            .ParamName.ShouldBe("request");
        Should.Throw<ArgumentNullException>(() => fixture.Ledger.RegisterDispatch(
                request,
                null!,
                "dispatch",
                InitialNow + TimeSpan.FromMinutes(2),
                "attempt",
                InitialNow + TimeSpan.FromMinutes(1)))
            .ParamName.ShouldBe("submittingPrincipal");
        Should.Throw<ArgumentOutOfRangeException>(() => fixture.Ledger.RegisterDispatch(
            request,
            fixture.Principal,
            "dispatch",
            InitialNow,
            "attempt",
            InitialNow + TimeSpan.FromMinutes(1)))
            .ParamName.ShouldBe("dispatchLeaseExpiresAtUtc");
        Should.Throw<ArgumentOutOfRangeException>(() => fixture.Ledger.RegisterDispatch(
            request,
            fixture.Principal,
            "dispatch",
            InitialNow + TimeSpan.FromMinutes(2),
            "attempt",
            InitialNow + TimeSpan.FromMinutes(3)))
            .ParamName.ShouldBe("intakeAttemptExpiresAtUtc");
        Should.Throw<ArgumentOutOfRangeException>(() => fixture.Ledger.RegisterDispatch(
            request,
            fixture.Principal,
            "dispatch",
            InitialNow + TimeSpan.FromMinutes(2),
            "attempt",
            InitialNow))
            .ParamName.ShouldBe("intakeAttemptExpiresAtUtc");
    }

    [Fact]
    public void ledger_dispatch_reference_byte_limit_accepts_exact_and_rejects_overlong_values()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var request = fixture.CreateRequest(envelope);
        var exact = new string('x', 256);

        Unwrap(fixture.Ledger.RegisterDispatch(
            request,
            fixture.Principal,
            exact,
            InitialNow + TimeSpan.FromMinutes(10),
            exact,
            InitialNow + TimeSpan.FromMinutes(5)));
        var secondRequest = fixture.CreateRequest(
            envelope,
            new OperationId(42, Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")));
        Should.Throw<ArgumentOutOfRangeException>(() => fixture.Ledger.RegisterDispatch(
                secondRequest,
                fixture.Principal,
                new string('x', 257),
                InitialNow + TimeSpan.FromMinutes(10),
                "attempt",
                InitialNow + TimeSpan.FromMinutes(5)))
            .ParamName.ShouldBe("dispatchLeaseId");
        var thirdRequest = fixture.CreateRequest(
            envelope,
            new OperationId(42, Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff")));
        Should.Throw<ArgumentOutOfRangeException>(() => fixture.Ledger.RegisterDispatch(
                thirdRequest,
                fixture.Principal,
                "dispatch",
                InitialNow + TimeSpan.FromMinutes(10),
                new string('x', 257),
                InitialNow + TimeSpan.FromMinutes(5)))
            .ParamName.ShouldBe("intakeAttemptId");
    }

    [Fact]
    public void dispatch_advance_validates_evidence_and_clamps_a_backward_clock()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        Unwrap(fixture.Ledger.RegisterDispatch(
            request,
            fixture.Principal,
            registration.DispatchLeaseId,
            registration.DispatchLeaseExpiresAtUtc,
            registration.IntakeAttemptId,
            registration.IntakeAttemptExpiresAtUtc));
        Should.Throw<ArgumentException>(() => fixture.Ledger.AdvanceDispatch(
                request.RepositoryId,
                request.OperationId,
                DispatchIntakePhase.Registered,
                DispatchIntakePhase.DispatchPinned,
                " "))
            .ParamName.ShouldBe("evidence");
        var backwardLedger = fixture.CreateLedger(() => InitialNow - TimeSpan.FromTicks(1));

        var advanced = Unwrap(backwardLedger.AdvanceDispatch(
            request.RepositoryId,
            request.OperationId,
            DispatchIntakePhase.Registered,
            DispatchIntakePhase.DispatchPinned,
            evidence: null));

        advanced.Transitions[1].CommittedAtUtc.ShouldBe(advanced.Transitions[0].CommittedAtUtc);
        advanced.Transitions[1].Evidence.ShouldBeNull();

        var forwardRequest = fixture.CreateRequest(
            envelope,
            new OperationId(42, Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")));
        Unwrap(fixture.Ledger.RegisterDispatch(
            forwardRequest,
            fixture.Principal,
            "forward-dispatch",
            InitialNow + TimeSpan.FromMinutes(10),
            "forward-attempt",
            InitialNow + TimeSpan.FromMinutes(5)));
        var forwardRecordPath = DurableOperationLedger.GetDispatchRecordPath(
            fixture.LedgerRoot,
            forwardRequest.RepositoryId,
            forwardRequest.OperationId);
        var bytesBeforeInvalidTransition = File.ReadAllBytes(forwardRecordPath);
        Error(fixture.Ledger.AdvanceDispatch(
            forwardRequest.RepositoryId,
            forwardRequest.OperationId,
            DispatchIntakePhase.Registered,
            DispatchIntakePhase.Prepared,
            "invalid direct preparation")).Kind.ShouldBe(OperationLedgerErrorKind.InvalidTransition);
        File.ReadAllBytes(forwardRecordPath).ShouldBe(bytesBeforeInvalidTransition);
        var forwardLedger = fixture.CreateLedger(() => InitialNow + TimeSpan.FromTicks(1));
        var forward = Unwrap(forwardLedger.AdvanceDispatch(
            forwardRequest.RepositoryId,
            forwardRequest.OperationId,
            DispatchIntakePhase.Registered,
            DispatchIntakePhase.DispatchPinned,
            evidence: null));
        forward.Transitions[1].CommittedAtUtc.ShouldBe(InitialNow + TimeSpan.FromTicks(1));
    }

    [Fact]
    public void first_registration_validates_every_plan_pin_constraint_before_writing_dispatch_state()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        var overlong = new string('x', ChangePlanStoreOptions.Default.MaximumPinReferenceBytes + 1);

        Should.Throw<ArgumentOutOfRangeException>(() => fixture.Coordinator.RegisterDispatchPlan(
                registration with { DispatchLeaseId = overlong }))
            .ParamName.ShouldBe("dispatchLeaseId");
        Should.Throw<ArgumentOutOfRangeException>(() => fixture.Coordinator.RegisterDispatchPlan(
                registration with { IntakeAttemptId = overlong }))
            .ParamName.ShouldBe("intakeAttemptId");
        Should.Throw<ArgumentOutOfRangeException>(() => fixture.Coordinator.RegisterDispatchPlan(
                registration with
                {
                    DispatchLeaseExpiresAtUtc = InitialNow
                                                + ChangePlanStoreOptions.Default.MaximumDispatchLeaseLifetime
                                                + TimeSpan.FromTicks(1)
                }))
            .ParamName.ShouldBe("dispatchLeaseExpiresAtUtc");

        Error(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Missing);
    }

    [Fact]
    public void first_registration_validates_the_derived_operation_pin_before_writing_dispatch_state()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var narrowOptions = ChangePlanStoreOptions.Default with { MaximumPinReferenceBytes = 16 };
        var coordinator = fixture.CreateCoordinator(planOptions: narrowOptions);

        Should.Throw<ArgumentOutOfRangeException>(() => coordinator.RegisterDispatchPlan(
                fixture.CreateRegistration(request)))
            .ParamName.ShouldBe("operationReference");
        Error(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Missing);
    }

    [Fact]
    public void restart_with_narrower_pin_limits_types_begin_intake_validation_failure()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        var narrowOptions = ChangePlanStoreOptions.Default with { MaximumPinReferenceBytes = 8 };
        var restarted = fixture.CreateCoordinator(planOptions: narrowOptions);

        Error(restarted.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CoordinationUnavailable);
    }

    [Fact]
    public void restart_with_narrower_pin_limits_types_operation_pin_validation_failure()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        var interrupted = fixture.CreateCoordinator(new DurableDispatchIntakeHooks(boundary =>
        {
            if (boundary == DispatchIntakeBoundary.ReceiptPhaseCommitted)
                throw new InvalidOperationException("Stop after the durable receipt phase.");
        }));
        Should.Throw<InvalidOperationException>(() =>
            Unwrap(interrupted.ReceiveAndPrepare(request, fixture.Principal)));
        var narrowOptions = ChangePlanStoreOptions.Default with { MaximumPinReferenceBytes = 16 };
        var restarted = fixture.CreateCoordinator(planOptions: narrowOptions);

        var rejected = Unwrap(restarted.ReceiveAndPrepare(request, fixture.Principal));

        rejected.Disposition.ShouldBe(DurableDispatchIntakeDisposition.RejectedPlanUnavailable);
        rejected.Operation!.CurrentState.ShouldBe(OperationLifecycleState.RejectedPlanUnavailable);
    }

    [Fact]
    public void dispatch_pin_clock_crossing_is_terminal_and_replayable_instead_of_poisoning_registration()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        var clockReads = 0;
        var coordinator = fixture.CreateCoordinator(
            planUtcNow: () => Interlocked.Increment(ref clockReads) <= 2
                ? InitialNow
                : registration.DispatchLeaseExpiresAtUtc);

        var rejected = Unwrap(coordinator.RegisterDispatchPlan(registration));

        rejected.Disposition.ShouldBe(DurableDispatchIntakeDisposition.RejectedPlanUnavailable);
        rejected.Dispatch!.CurrentPhase.ShouldBe(DispatchIntakePhase.RejectedPlanUnavailable);
        Error(fixture.Ledger.Load(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Missing);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(registration)).Disposition
            .ShouldBe(DurableDispatchIntakeDisposition.RejectedPlanUnavailable);
    }

    [Fact]
    public void conflicting_retained_dispatch_pin_binding_is_typed_as_corrupt_coordination()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        Unwrap(fixture.PlanStore.PinDispatchLease(
            key,
            registration.DispatchLeaseId,
            registration.DispatchLeaseExpiresAtUtc - TimeSpan.FromTicks(1)));
        Unwrap(fixture.Ledger.RegisterDispatch(
            request,
            fixture.Principal,
            registration.DispatchLeaseId,
            registration.DispatchLeaseExpiresAtUtc,
            registration.IntakeAttemptId,
            registration.IntakeAttemptExpiresAtUtc));

        Error(fixture.Coordinator.RegisterDispatchPlan(registration)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);
    }

    [Fact]
    public void conflicting_retained_intake_pin_binding_is_typed_as_corrupt_coordination()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        Unwrap(fixture.PlanStore.PinDispatchLease(
            key,
            "other-dispatch",
            registration.DispatchLeaseExpiresAtUtc));
        Unwrap(fixture.PlanStore.BeginIntakePin(key, "other-dispatch", registration.IntakeAttemptId));
        Unwrap(fixture.Ledger.RegisterDispatch(
            request,
            fixture.Principal,
            registration.DispatchLeaseId,
            registration.DispatchLeaseExpiresAtUtc,
            registration.IntakeAttemptId,
            registration.IntakeAttemptExpiresAtUtc));
        Unwrap(fixture.Ledger.AdvanceDispatch(
            request.RepositoryId,
            request.OperationId,
            DispatchIntakePhase.Registered,
            DispatchIntakePhase.DispatchPinned,
            "test frontier"));

        Error(fixture.Coordinator.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);
    }

    [Fact]
    public void unrelated_retained_intake_pin_does_not_satisfy_expected_pin_readback()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(registration));
        Unwrap(fixture.PlanStore.ReleasePin(
            key,
            ChangePlanPinKind.DispatchLease,
            registration.DispatchLeaseId));
        Unwrap(fixture.PlanStore.PinDispatchLease(
            key,
            "other-dispatch",
            registration.DispatchLeaseExpiresAtUtc));
        Unwrap(fixture.PlanStore.BeginIntakePin(key, "other-dispatch", "other-attempt"));

        var rejected = Unwrap(fixture.Coordinator.ReceiveAndPrepare(request, fixture.Principal));

        rejected.Disposition.ShouldBe(DurableDispatchIntakeDisposition.RejectedPlanUnavailable);
        rejected.Operation!.CurrentState.ShouldBe(OperationLifecycleState.RejectedPlanUnavailable);
    }

    [Fact]
    public void registered_exact_plan_is_received_prepared_and_replayed_without_pin_or_history_churn()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);

        var ready = Unwrap(fixture.Coordinator.RegisterDispatchPlan(registration));
        ready.Disposition.ShouldBe(DurableDispatchIntakeDisposition.Ready);

        var prepared = Unwrap(fixture.Coordinator.ReceiveAndPrepare(request, fixture.Principal));
        prepared.Disposition.ShouldBe(DurableDispatchIntakeDisposition.Prepared);
        prepared.Operation!.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        prepared.Operation.Transitions.Select(transition => transition.State).ShouldBe([
            OperationLifecycleState.Received,
            OperationLifecycleState.Prepared
        ]);
        var preparedBridge = Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId));
        preparedBridge.CurrentPhase.ShouldBe(DispatchIntakePhase.Prepared);
        var preparedPlan = Unwrap(fixture.PlanStore.Load(key));
        preparedPlan.EnvelopeBytes.ShouldBe(envelope.SerializeEnvelope());
        preparedPlan.Pins.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            pin => pin.Kind.ShouldBe(ChangePlanPinKind.OperationLedger),
            pin => pin.ReferenceId.ShouldBe(
                DurableDispatchIntakeCoordinator.FormatOperationReference(request.OperationId)),
            pin => pin.ExpiresAtUtc.ShouldBeNull(),
            pin => pin.AdoptedFromDispatchLeaseId.ShouldBe(registration.DispatchLeaseId));

        var replay = Unwrap(fixture.Coordinator.ReceiveAndPrepare(request, fixture.Principal));
        replay.Disposition.ShouldBe(DurableDispatchIntakeDisposition.Replay);
        replay.Operation!.ShouldSatisfyAllConditions(
            operation => operation.RequestIdentity.ShouldBe(prepared.Operation.RequestIdentity),
            operation => operation.CurrentState.ShouldBe(prepared.Operation.CurrentState),
            operation => operation.Transitions.ToArray().ShouldBe(prepared.Operation.Transitions.ToArray()));
        Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId))
            .Transitions.ToArray().ShouldBe(preparedBridge.Transitions.ToArray());
        Unwrap(fixture.PlanStore.Load(key)).Pins.ToArray().ShouldBe(preparedPlan.Pins.ToArray());
    }

    [Fact]
    public void a_different_request_under_the_same_operation_conflicts_before_its_plan_is_pinned()
    {
        using var fixture = new CoordinatorFixture();
        var firstEnvelope = fixture.CreateEnvelope();
        var secondEnvelope = fixture.CreateEnvelope(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        Unwrap(fixture.PlanStore.Persist(firstEnvelope));
        var secondKey = Unwrap(fixture.PlanStore.Persist(secondEnvelope));
        var firstRequest = fixture.CreateRequest(firstEnvelope);
        var secondRequest = fixture.CreateRequest(secondEnvelope, firstRequest.OperationId);

        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(firstRequest)));
        var conflict = Unwrap(fixture.Coordinator.RegisterDispatchPlan(
            fixture.CreateRegistration(secondRequest, "dispatch-2", "attempt-2")));

        conflict.Disposition.ShouldBe(DurableDispatchIntakeDisposition.Conflict);
        conflict.Dispatch.ShouldBeNull();
        Unwrap(fixture.PlanStore.Load(secondKey)).Pins.ShouldBeEmpty();
        Unwrap(fixture.Ledger.LoadDispatch(firstRequest.RepositoryId, firstRequest.OperationId))
            .RequestIdentity.ShouldBe(firstRequest.Identity);
    }

    [Fact]
    public void another_principal_is_denied_without_operation_disclosure_or_mutation()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(registration));
        var beforeDispatch = Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId));
        var beforePins = Unwrap(fixture.PlanStore.Load(key)).Pins;

        var denied = Unwrap(fixture.Coordinator.ReceiveAndPrepare(
            request,
            new AuthenticatedPrincipalId("principal-002")));

        denied.Disposition.ShouldBe(DurableDispatchIntakeDisposition.AccessDenied);
        denied.Dispatch.ShouldBeNull();
        denied.Operation.ShouldBeNull();
        var afterDispatch = Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId));
        afterDispatch.ShouldSatisfyAllConditions(
            dispatch => dispatch.RequestIdentity.ShouldBe(beforeDispatch.RequestIdentity),
            dispatch => dispatch.CurrentPhase.ShouldBe(beforeDispatch.CurrentPhase),
            dispatch => dispatch.Transitions.ToArray().ShouldBe(beforeDispatch.Transitions.ToArray()));
        Unwrap(fixture.PlanStore.Load(key)).Pins.ToArray().ShouldBe(beforePins.ToArray());
        Error(fixture.Ledger.Load(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Missing);

        var registrationDenied = Unwrap(fixture.Coordinator.RegisterDispatchPlan(
            fixture.CreateRegistration(request) with
            {
                SubmittingPrincipal = new AuthenticatedPrincipalId("principal-002")
            }));
        registrationDenied.Disposition.ShouldBe(DurableDispatchIntakeDisposition.AccessDenied);
        registrationDenied.Dispatch.ShouldBeNull();
    }

    [Fact]
    public void unavailable_plan_is_fenced_before_dispatch_without_creating_a_receipt()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var request = fixture.CreateRequest(envelope);

        var rejected = Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));

        rejected.Disposition.ShouldBe(DurableDispatchIntakeDisposition.RejectedPlanUnavailable);
        rejected.Dispatch!.CurrentPhase.ShouldBe(DispatchIntakePhase.RejectedPlanUnavailable);
        Error(fixture.Ledger.Load(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Missing);
        var replay = Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        replay.Disposition.ShouldBe(DurableDispatchIntakeDisposition.RejectedPlanUnavailable);
        replay.Dispatch!.ShouldSatisfyAllConditions(
            dispatch => dispatch.RequestIdentity.ShouldBe(rejected.Dispatch.RequestIdentity),
            dispatch => dispatch.CurrentPhase.ShouldBe(rejected.Dispatch.CurrentPhase),
            dispatch => dispatch.Transitions.ToArray().ShouldBe(rejected.Dispatch.Transitions.ToArray()));
        var receiveReplay = Unwrap(fixture.Coordinator.ReceiveAndPrepare(request, fixture.Principal));
        receiveReplay.Disposition.ShouldBe(DurableDispatchIntakeDisposition.RejectedPlanUnavailable);
        receiveReplay.Operation.ShouldBeNull();
    }

    [Fact]
    public void receive_without_registered_dispatch_is_indeterminate_and_has_no_side_effect()
    {
        using var fixture = new CoordinatorFixture();
        var request = fixture.CreateRequest(fixture.CreateEnvelope());

        var result = Unwrap(fixture.Coordinator.ReceiveAndPrepare(request, fixture.Principal));

        result.Disposition.ShouldBe(DurableDispatchIntakeDisposition.Indeterminate);
        result.Dispatch.ShouldBeNull();
        result.Operation.ShouldBeNull();
        Directory.Exists(fixture.PlanStoreRoot).ShouldBeFalse();
    }

    [Fact]
    public void prepared_registration_replay_returns_the_retained_operation_without_churn()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(registration));
        var prepared = Unwrap(fixture.Coordinator.ReceiveAndPrepare(request, fixture.Principal));
        var pins = Unwrap(fixture.PlanStore.Load(key)).Pins.ToArray();

        var replay = Unwrap(fixture.Coordinator.RegisterDispatchPlan(
            registration with
            {
                DispatchLeaseId = "ignored-replay-lease",
                IntakeAttemptId = "ignored-replay-attempt"
            }));

        replay.Disposition.ShouldBe(DurableDispatchIntakeDisposition.Replay);
        replay.Operation!.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        replay.Operation.Transitions.ToArray().ShouldBe(prepared.Operation!.Transitions.ToArray());
        Unwrap(fixture.PlanStore.Load(key)).Pins.ToArray().ShouldBe(pins);
    }

    [Fact]
    public void prepared_registration_replay_ignores_expired_transport_metadata()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(registration));
        Unwrap(fixture.Coordinator.ReceiveAndPrepare(request, fixture.Principal));
        var afterExpiry = registration.DispatchLeaseExpiresAtUtc + TimeSpan.FromMinutes(1);
        var restarted = fixture.CreateCoordinator(
            planUtcNow: () => afterExpiry,
            ledgerUtcNow: () => afterExpiry);

        var replay = Unwrap(restarted.RegisterDispatchPlan(registration));

        replay.Disposition.ShouldBe(DurableDispatchIntakeDisposition.Replay);
        replay.Operation!.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
    }

    [Fact]
    public void pinned_dispatch_survives_plan_expiry_between_pin_commit_and_phase_commit()
    {
        using var fixture = new CoordinatorFixture();
        var planExpiry = InitialNow + TimeSpan.FromMinutes(2);
        var envelope = fixture.CreateEnvelope(expiresAtUtc: planExpiry);
        var key = Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        var interrupted = fixture.CreateCoordinator(new DurableDispatchIntakeHooks(boundary =>
        {
            if (boundary == DispatchIntakeBoundary.DispatchPlanPinned)
                throw new InvalidOperationException("Simulated termination after the pin commit.");
        }));
        Should.Throw<InvalidOperationException>(() => interrupted.RegisterDispatchPlan(registration));
        var afterPlanExpiry = planExpiry + TimeSpan.FromMinutes(1);
        var restarted = fixture.CreateCoordinator(
            planUtcNow: () => afterPlanExpiry,
            ledgerUtcNow: () => afterPlanExpiry);

        Unwrap(restarted.RegisterDispatchPlan(registration)).Disposition
            .ShouldBe(DurableDispatchIntakeDisposition.Ready);
        var prepared = Unwrap(restarted.ReceiveAndPrepare(request, fixture.Principal));

        prepared.Operation!.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        Unwrap(fixture.PlanStore.LoadRetained(key)).Pins.ShouldHaveSingleItem().Kind
            .ShouldBe(ChangePlanPinKind.OperationLedger);
    }

    [Fact]
    public void every_post_replace_pin_failure_is_reconciled_from_exact_durable_pin_state()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var durability = new FaultAfterEveryReplaceDurability();
        var coordinator = fixture.CreateCoordinator(planDurability: durability);

        Unwrap(coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request))).Disposition
            .ShouldBe(DurableDispatchIntakeDisposition.Ready);
        var prepared = Unwrap(coordinator.ReceiveAndPrepare(request, fixture.Principal));

        prepared.Disposition.ShouldBe(DurableDispatchIntakeDisposition.Prepared);
        prepared.Operation!.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        durability.InjectedFailureCount.ShouldBe(3);
        Unwrap(fixture.PlanStore.Load(key)).Pins.ShouldHaveSingleItem().Kind
            .ShouldBe(ChangePlanPinKind.OperationLedger);
    }

    [Fact]
    public void unreadable_pin_readback_remains_indeterminate_and_does_not_terminally_reject()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        var planPath = Directory.EnumerateFiles(
            fixture.PlanStoreRoot,
            "*.plan",
            SearchOption.AllDirectories).Single();
        File.WriteAllText(planPath, "not-json");

        Error(fixture.Coordinator.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CoordinationUnavailable);
        Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId))
            .CurrentPhase.ShouldBe(DispatchIntakePhase.DispatchPinned);
        Error(fixture.Ledger.Load(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Missing);
        key.PlanId.ShouldBe(envelope.Payload.PlanId);
    }

    [Fact]
    public void every_post_replace_ledger_failure_is_reconciled_from_exact_durable_state()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var durability = new FaultAfterEveryLedgerReplaceDurability();
        var coordinator = fixture.CreateCoordinator(ledgerDurability: durability);

        Unwrap(coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request))).Disposition
            .ShouldBe(DurableDispatchIntakeDisposition.Ready);
        var prepared = Unwrap(coordinator.ReceiveAndPrepare(request, fixture.Principal));

        prepared.Disposition.ShouldBe(DurableDispatchIntakeDisposition.Replay);
        prepared.Operation!.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        durability.InjectedFailureCount.ShouldBe(8);
    }

    [Fact]
    public void unreadable_dispatch_pin_readback_remains_indeterminate_without_terminal_rejection()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var coordinator = fixture.CreateCoordinator(
            planDurability: new FailBeforePlanReplaceDurability(
                failOnReplacement: 1,
                beforeFailure: (_, recordPath) => File.WriteAllText(recordPath, "not-json")));

        Error(coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request))).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CoordinationUnavailable);
        Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId))
            .CurrentPhase.ShouldBe(DispatchIntakePhase.Registered);
    }

    [Fact]
    public void lost_dispatch_during_operation_pin_reconciliation_is_indeterminate()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        var dispatchPath = DurableOperationLedger.GetDispatchRecordPath(
            fixture.LedgerRoot,
            request.RepositoryId,
            request.OperationId);
        var coordinator = fixture.CreateCoordinator(
            planDurability: new FailBeforePlanReplaceDurability(
                failOnReplacement: 2,
                beforeFailure: (_, _) => File.Delete(dispatchPath)));

        Error(coordinator.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CoordinationUnavailable);
        Error(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Missing);
        Unwrap(fixture.Ledger.Load(request.RepositoryId, request.OperationId)).CurrentState
            .ShouldBe(OperationLifecycleState.Received);
        Unwrap(fixture.PlanStore.LoadRetained(key)).Pins.ShouldHaveSingleItem().Kind
            .ShouldBe(ChangePlanPinKind.IntakePending);
    }

    [Fact]
    public void concurrent_progress_after_a_missing_operation_pin_readback_converges_to_prepared()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        var concurrentRan = false;
        var coordinator = fixture.CreateCoordinator(
            hooks: new DurableDispatchIntakeHooks(
                AfterMissingOperationPinReadback: () =>
                {
                    concurrentRan = true;
                    Unwrap(fixture.CreateCoordinator().ReceiveAndPrepare(request, fixture.Principal));
                }),
            planDurability: new FailBeforePlanReplaceDurability(
                failOnReplacement: 2,
                beforeFailure: (_, _) => { }));

        var replay = Unwrap(coordinator.ReceiveAndPrepare(request, fixture.Principal));

        concurrentRan.ShouldBeTrue();
        replay.Disposition.ShouldBe(DurableDispatchIntakeDisposition.Replay);
        replay.Operation!.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId))
            .CurrentPhase.ShouldBe(DispatchIntakePhase.Prepared);
    }

    [Fact]
    public void post_replace_rejection_failures_reconcile_both_durable_frontiers()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        File.Delete(Directory.EnumerateFiles(
            fixture.PlanStoreRoot,
            "*.plan",
            SearchOption.AllDirectories).Single());
        var durability = new FaultAfterEveryLedgerReplaceDurability();
        var coordinator = fixture.CreateCoordinator(ledgerDurability: durability);

        var rejected = Unwrap(coordinator.ReceiveAndPrepare(request, fixture.Principal));

        rejected.Disposition.ShouldBe(DurableDispatchIntakeDisposition.RejectedPlanUnavailable);
        rejected.Operation!.CurrentState.ShouldBe(OperationLifecycleState.RejectedPlanUnavailable);
        rejected.Dispatch!.CurrentPhase.ShouldBe(DispatchIntakePhase.RejectedPlanUnavailable);
        durability.InjectedFailureCount.ShouldBe(3);
    }

    [Fact]
    public void receipt_deleted_before_rejection_is_cross_record_corruption()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        File.Delete(Directory.EnumerateFiles(
            fixture.PlanStoreRoot,
            "*.plan",
            SearchOption.AllDirectories).Single());
        var operationPath = DurableOperationLedger.GetRecordPath(
            fixture.LedgerRoot,
            request.RepositoryId,
            request.OperationId);
        var coordinator = fixture.CreateCoordinator(new DurableDispatchIntakeHooks(boundary =>
        {
            if (boundary == DispatchIntakeBoundary.ReceiptCommitted)
                File.Delete(operationPath);
        }));

        Error(coordinator.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);
        Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId))
            .CurrentPhase.ShouldBe(DispatchIntakePhase.DispatchPinned);
    }

    [Fact]
    public void rejection_receipt_read_preserves_unsafe_protection()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        File.Delete(Directory.EnumerateFiles(
            fixture.PlanStoreRoot,
            "*.plan",
            SearchOption.AllDirectories).Single());
        var authority = new SwitchableLedgerProtectionAuthority(fixture.LedgerRoot);
        var coordinator = new DurableDispatchIntakeCoordinator(
            fixture.PlanStore,
            new DurableOperationLedger(
                fixture.LedgerRoot,
                () => InitialNow,
                authority),
            new DurableDispatchIntakeHooks(boundary =>
            {
                if (boundary == DispatchIntakeBoundary.ReceiptCommitted)
                    authority.Deny();
            }));

        Error(coordinator.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.UnsafeProtection);
        Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId))
            .CurrentPhase.ShouldBe(DispatchIntakePhase.DispatchPinned);
    }

    [Fact]
    public void receipt_deleted_during_uncertain_rejection_is_cross_record_corruption()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        File.Delete(Directory.EnumerateFiles(
            fixture.PlanStoreRoot,
            "*.plan",
            SearchOption.AllDirectories).Single());
        var coordinator = fixture.CreateCoordinator(
            ledgerDurability: new FailAfterLedgerReplaceDurability(
                2,
                File.Delete));

        Error(coordinator.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);
    }

    [Fact]
    public void uncertain_rejection_revalidates_a_replaced_receipt_binding()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var mismatchedRequest = fixture.CreateRequest(
            fixture.CreateEnvelope(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")),
            request.OperationId);
        Unwrap(fixture.Ledger.Receive(mismatchedRequest, fixture.Principal));
        Unwrap(fixture.Ledger.AppendTransition(
            request.RepositoryId,
            request.OperationId,
            OperationLifecycleState.Received,
            OperationLifecycleState.RejectedPlanUnavailable,
            "test mismatched rejection"));
        var operationPath = DurableOperationLedger.GetRecordPath(
            fixture.LedgerRoot,
            request.RepositoryId,
            request.OperationId);
        var mismatchedRejection = File.ReadAllBytes(operationPath);
        File.Delete(operationPath);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        File.Delete(Directory.EnumerateFiles(
            fixture.PlanStoreRoot,
            "*.plan",
            SearchOption.AllDirectories).Single());
        var coordinator = fixture.CreateCoordinator(
            ledgerDurability: new FailAfterLedgerReplaceDurability(
                2,
                _ => File.WriteAllBytes(operationPath, mismatchedRejection)));

        Error(coordinator.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);
        Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId))
            .CurrentPhase.ShouldBe(DispatchIntakePhase.DispatchPinned);
    }

    [Theory]
    [InlineData("operation", 2)]
    [InlineData("dispatch", 3)]
    public void contradictory_rejection_frontiers_are_typed_as_corrupt(
        string target,
        int failOnReplacement)
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        File.Delete(Directory.EnumerateFiles(
            fixture.PlanStoreRoot,
            "*.plan",
            SearchOption.AllDirectories).Single());
        var durability = new RewriteAndFailAfterLedgerReplaceDurability(
            failOnReplacement,
            record => RewriteRejectionFrontier(record, target));
        var coordinator = fixture.CreateCoordinator(ledgerDurability: durability);

        Error(coordinator.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);
    }

    [Fact]
    public void registration_retry_preserves_unsafe_protection_from_the_authoritative_retry()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var authority = new SwitchableLedgerProtectionAuthority(fixture.LedgerRoot);
        var durability = new FailAfterLedgerReplaceDurability(1, _ => authority.Deny());
        var coordinator = new DurableDispatchIntakeCoordinator(
            fixture.PlanStore,
            new DurableOperationLedger(
                fixture.LedgerRoot,
                () => InitialNow,
                authority,
                durability));

        Error(coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request))).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.UnsafeProtection);
    }

    [Fact]
    public void dispatch_phase_reconciliation_preserves_unsafe_protection_from_readback()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var authority = new SwitchableLedgerProtectionAuthority(fixture.LedgerRoot);
        var durability = new FailAfterLedgerReplaceDurability(2, _ => authority.Deny());
        var coordinator = new DurableDispatchIntakeCoordinator(
            fixture.PlanStore,
            new DurableOperationLedger(
                fixture.LedgerRoot,
                () => InitialNow,
                authority,
                durability));

        Error(coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request))).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.UnsafeProtection);
    }

    [Theory]
    [InlineData("prepared-operation", 4)]
    [InlineData("rejected-operation", 2)]
    [InlineData("rejected-dispatch", 3)]
    public void lifecycle_and_rejection_reconciliation_preserve_unsafe_protection_from_readback(
        string frontier,
        int failOnReplacement)
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        if (frontier.StartsWith("rejected", StringComparison.Ordinal))
        {
            File.Delete(Directory.EnumerateFiles(
                fixture.PlanStoreRoot,
                "*.plan",
                SearchOption.AllDirectories).Single());
        }
        var authority = new SwitchableLedgerProtectionAuthority(fixture.LedgerRoot);
        var durability = new FailAfterLedgerReplaceDurability(
            failOnReplacement,
            _ => authority.Deny());
        var coordinator = new DurableDispatchIntakeCoordinator(
            fixture.PlanStore,
            new DurableOperationLedger(
                fixture.LedgerRoot,
                () => InitialNow,
                authority,
                durability));

        Error(coordinator.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.UnsafeProtection);
    }

    [Theory]
    [InlineData("dispatch-phase", 2)]
    [InlineData("prepared-operation", 4)]
    [InlineData("rejected-operation", 2)]
    [InlineData("rejected-dispatch", 3)]
    public void uncertain_replacement_preserves_corrupt_state_from_authoritative_readback(
        string frontier,
        int failOnReplacement)
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        if (frontier == "dispatch-phase")
        {
            var coordinator = fixture.CreateCoordinator(
                ledgerDurability: CorruptAfterLedgerReplace(failOnReplacement));

            Error(coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request))).Kind
                .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);
            return;
        }

        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        if (frontier.StartsWith("rejected", StringComparison.Ordinal))
        {
            File.Delete(Directory.EnumerateFiles(
                fixture.PlanStoreRoot,
                "*.plan",
                SearchOption.AllDirectories).Single());
        }
        var resumed = fixture.CreateCoordinator(
            ledgerDurability: CorruptAfterLedgerReplace(failOnReplacement));

        Error(resumed.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);
    }

    [Fact]
    public void existing_prepared_operation_cannot_be_reclassified_as_plan_unavailable()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        Unwrap(fixture.Ledger.Receive(request, fixture.Principal));
        Unwrap(fixture.Ledger.AppendTransition(
            request.RepositoryId,
            request.OperationId,
            OperationLifecycleState.Received,
            OperationLifecycleState.Prepared,
            "test concurrent preparation"));
        File.Delete(Directory.EnumerateFiles(
            fixture.PlanStoreRoot,
            "*.plan",
            SearchOption.AllDirectories).Single());

        Error(fixture.Coordinator.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);
    }

    [Fact]
    public void pre_replace_dispatch_phase_failure_returns_coordination_error_without_false_progress()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var coordinator = fixture.CreateCoordinator(
            ledgerDurability: new FailBeforeLedgerReplaceDurability(failOnReplacement: 2));

        Error(coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request))).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CoordinationUnavailable);

        Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId))
            .CurrentPhase.ShouldBe(DispatchIntakePhase.Registered);
        Unwrap(fixture.PlanStore.Load(key)).Pins.ShouldHaveSingleItem().Kind
            .ShouldBe(ChangePlanPinKind.DispatchLease);
    }

    [Fact]
    public void pre_replace_rejection_phase_failure_returns_coordination_error_without_false_progress()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var request = fixture.CreateRequest(envelope);
        var coordinator = fixture.CreateCoordinator(
            ledgerDurability: new FailBeforeLedgerReplaceDurability(failOnReplacement: 2));

        Error(coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request))).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CoordinationUnavailable);
        Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId))
            .CurrentPhase.ShouldBe(DispatchIntakePhase.Registered);
    }

    [Theory]
    [InlineData(2, (int)OperationLifecycleState.Received)]
    [InlineData(3, (int)OperationLifecycleState.RejectedPlanUnavailable)]
    public void pre_replace_received_rejection_failures_retain_the_last_proven_frontier(
        int failOnReplacement,
        int expectedOperationState)
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        File.Delete(Directory.EnumerateFiles(
            fixture.PlanStoreRoot,
            "*.plan",
            SearchOption.AllDirectories).Single());
        var coordinator = fixture.CreateCoordinator(
            ledgerDurability: new FailBeforeLedgerReplaceDurability(failOnReplacement));

        Error(coordinator.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CoordinationUnavailable);

        Unwrap(fixture.Ledger.Load(request.RepositoryId, request.OperationId)).CurrentState
            .ShouldBe((OperationLifecycleState)expectedOperationState);
        Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId))
            .CurrentPhase.ShouldBe(DispatchIntakePhase.DispatchPinned);
    }

    [Fact]
    public void receive_with_different_semantic_request_conflicts_without_disclosing_the_dispatch()
    {
        using var fixture = new CoordinatorFixture();
        var firstEnvelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(firstEnvelope));
        var firstRequest = fixture.CreateRequest(firstEnvelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(firstRequest)));
        var conflictingEnvelope = fixture.CreateEnvelope(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        var conflictingRequest = fixture.CreateRequest(conflictingEnvelope, firstRequest.OperationId);

        var conflict = Unwrap(fixture.Coordinator.ReceiveAndPrepare(
            conflictingRequest,
            fixture.Principal));

        conflict.Disposition.ShouldBe(DurableDispatchIntakeDisposition.Conflict);
        conflict.Dispatch.ShouldBeNull();
        conflict.Operation.ShouldBeNull();
    }

    [Fact]
    public void missing_protection_authority_fails_closed_at_both_coordination_stores()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var unprotectedLedgerCoordinator = new DurableDispatchIntakeCoordinator(
            fixture.PlanStore,
            new DurableOperationLedger(fixture.LedgerRoot));

        Error(unprotectedLedgerCoordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)))
            .Kind.ShouldBe(DurableDispatchIntakeErrorKind.UnsafeProtection);
        Error(unprotectedLedgerCoordinator.ReceiveAndPrepare(request, fixture.Principal))
            .Kind.ShouldBe(DurableDispatchIntakeErrorKind.UnsafeProtection);

        var unprotectedPlanCoordinator = new DurableDispatchIntakeCoordinator(
            new DurableChangePlanStore(fixture.PlanStoreRoot, utcNow: () => InitialNow),
            fixture.Ledger);
        Error(unprotectedPlanCoordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)))
            .Kind.ShouldBe(DurableDispatchIntakeErrorKind.UnsafeProtection);
        Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId))
            .CurrentPhase.ShouldBe(DispatchIntakePhase.Registered);
    }

    [Fact]
    public async Task concurrent_identical_intake_commits_one_receipt_and_one_prepared_transition()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));

        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            Unwrap(fixture.CreateCoordinator().ReceiveAndPrepare(request, fixture.Principal)))));

        attempts.ShouldAllBe(result =>
            result.Disposition == DurableDispatchIntakeDisposition.Prepared
                || result.Disposition == DurableDispatchIntakeDisposition.Replay);
        attempts.Count(result => result.Disposition == DurableDispatchIntakeDisposition.Prepared)
            .ShouldBeLessThanOrEqualTo(1);
        Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId))
            .CurrentPhase.ShouldBe(DispatchIntakePhase.Prepared);
        var operation = Unwrap(fixture.Ledger.Load(request.RepositoryId, request.OperationId));
        operation.Transitions.Select(transition => transition.State).ShouldBe([
            OperationLifecycleState.Received,
            OperationLifecycleState.Prepared
        ]);
    }

    [Fact]
    public void plan_loss_after_dispatch_is_received_as_terminal_plan_unavailable_without_effect()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        File.Delete(Directory.EnumerateFiles(
            fixture.PlanStoreRoot,
            "*.plan",
            SearchOption.AllDirectories).Single());

        var rejected = Unwrap(fixture.Coordinator.ReceiveAndPrepare(request, fixture.Principal));

        rejected.Disposition.ShouldBe(DurableDispatchIntakeDisposition.RejectedPlanUnavailable);
        rejected.Dispatch!.CurrentPhase.ShouldBe(DispatchIntakePhase.RejectedPlanUnavailable);
        rejected.Operation!.CurrentState.ShouldBe(OperationLifecycleState.RejectedPlanUnavailable);
        rejected.Operation.Transitions.Select(transition => transition.State).ShouldBe([
            OperationLifecycleState.Received,
            OperationLifecycleState.RejectedPlanUnavailable
        ]);
        var replay = Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        replay.Disposition.ShouldBe(DurableDispatchIntakeDisposition.RejectedPlanUnavailable);
        replay.Operation!.CurrentState.ShouldBe(OperationLifecycleState.RejectedPlanUnavailable);
        File.Exists(Path.Combine(fixture.RepositoryRoot, "src", "Added.cs")).ShouldBeFalse();
    }

    [Fact]
    public async Task concurrent_plan_unavailable_intake_converges_every_caller_on_one_rejection()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        File.Delete(Directory.EnumerateFiles(
            fixture.PlanStoreRoot,
            "*.plan",
            SearchOption.AllDirectories).Single());

        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            Unwrap(fixture.CreateCoordinator().ReceiveAndPrepare(request, fixture.Principal)))));

        attempts.ShouldAllBe(result =>
            result.Disposition == DurableDispatchIntakeDisposition.RejectedPlanUnavailable);
        var operation = Unwrap(fixture.Ledger.Load(request.RepositoryId, request.OperationId));
        operation.Transitions.Select(transition => transition.State).ShouldBe([
            OperationLifecycleState.Received,
            OperationLifecycleState.RejectedPlanUnavailable
        ]);
    }

    [Fact]
    public void revision_mismatch_is_fenced_before_the_plan_is_pinned_or_received()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(fixture.PlanStore.Persist(envelope));
        var claims = fixture.CreateClaims(envelope);
        var request = CanonicalApplyRequest.Create(
            envelope.Payload.Repository.RepositoryId,
            new OperationId(42, Guid.Parse("99999999-8888-7777-6666-555555555555")),
            envelope.Payload.PlanId,
            envelope.PlanHash,
            "different-revision",
            claims);

        var rejected = Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));

        rejected.Disposition.ShouldBe(DurableDispatchIntakeDisposition.RejectedPlanUnavailable);
        Unwrap(fixture.PlanStore.Load(key)).Pins.ShouldBeEmpty();
        Error(fixture.Ledger.Load(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Missing);
    }

    [Fact]
    public void receive_reconciles_a_registered_dispatch_to_terminal_plan_unavailable()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        Unwrap(fixture.Ledger.RegisterDispatch(
            request,
            fixture.Principal,
            registration.DispatchLeaseId,
            registration.DispatchLeaseExpiresAtUtc,
            registration.IntakeAttemptId,
            registration.IntakeAttemptExpiresAtUtc));

        var rejected = Unwrap(fixture.Coordinator.ReceiveAndPrepare(request, fixture.Principal));

        rejected.Disposition.ShouldBe(DurableDispatchIntakeDisposition.RejectedPlanUnavailable);
        rejected.Dispatch!.CurrentPhase.ShouldBe(DispatchIntakePhase.RejectedPlanUnavailable);
        rejected.Operation.ShouldBeNull();
    }

    [Fact]
    public void receive_propagates_unsafe_plan_protection_while_reconciling_registered_dispatch()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        Unwrap(fixture.Ledger.RegisterDispatch(
            request,
            fixture.Principal,
            registration.DispatchLeaseId,
            registration.DispatchLeaseExpiresAtUtc,
            registration.IntakeAttemptId,
            registration.IntakeAttemptExpiresAtUtc));
        var coordinator = new DurableDispatchIntakeCoordinator(
            new DurableChangePlanStore(fixture.PlanStoreRoot, utcNow: () => InitialNow),
            fixture.Ledger);

        Error(coordinator.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.UnsafeProtection);
        Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId))
            .CurrentPhase.ShouldBe(DispatchIntakePhase.Registered);
    }

    [Fact]
    public void corrupt_dispatch_phase_is_typed_and_never_rewritten_by_replay()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        var path = DurableOperationLedger.GetDispatchRecordPath(
            fixture.LedgerRoot,
            request.RepositoryId,
            request.OperationId);
        var record = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        record["currentPhase"] = 999;
        var corrupted = Encoding.UTF8.GetBytes(record.ToJsonString());
        File.WriteAllBytes(path, corrupted);

        Error(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
        var registration = fixture.CreateRegistration(request);
        Error(fixture.Ledger.RegisterDispatch(
            request,
            fixture.Principal,
            registration.DispatchLeaseId,
            registration.DispatchLeaseExpiresAtUtc,
            registration.IntakeAttemptId,
            registration.IntakeAttemptExpiresAtUtc)).Kind.ShouldBe(OperationLedgerErrorKind.Corrupt);
        Error(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request))).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);
        File.ReadAllBytes(path).ShouldBe(corrupted);
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("schema")]
    [InlineData("request-hash")]
    [InlineData("canonical-metadata")]
    public void malformed_dispatch_payloads_are_typed_as_corrupt(string mutation)
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        Unwrap(fixture.Ledger.RegisterDispatch(
            request,
            fixture.Principal,
            registration.DispatchLeaseId,
            registration.DispatchLeaseExpiresAtUtc,
            registration.IntakeAttemptId,
            registration.IntakeAttemptExpiresAtUtc));
        var path = DurableOperationLedger.GetDispatchRecordPath(
            fixture.LedgerRoot,
            request.RepositoryId,
            request.OperationId);
        if (mutation == "invalid-json")
        {
            File.WriteAllText(path, "not-json");
        }
        else
        {
            var record = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
            switch (mutation)
            {
                case "schema":
                    record["schemaVersion"] = 999;
                    break;
                case "request-hash":
                    record["requestHash"] = new string('0', 64);
                    break;
                case "canonical-metadata":
                    record["expectedRevision"] = new string('c', 40);
                    break;
                default:
                    throw new ShouldAssertException($"Unsupported dispatch payload mutation '{mutation}'.");
            }
            File.WriteAllText(path, record.ToJsonString());
        }

        Error(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
    }

    [Fact]
    public void unix_dispatch_directory_is_repaired_to_owner_private_before_registration()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new CoordinatorFixture();
        var dispatchRoot = Path.Combine(fixture.LedgerRoot, "dispatch");
        Directory.CreateDirectory(dispatchRoot);
        File.SetUnixFileMode(
            dispatchRoot,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        var request = fixture.CreateRequest(fixture.CreateEnvelope());
        var registration = fixture.CreateRegistration(request);

        Unwrap(fixture.Ledger.RegisterDispatch(
            request,
            fixture.Principal,
            registration.DispatchLeaseId,
            registration.DispatchLeaseExpiresAtUtc,
            registration.IntakeAttemptId,
            registration.IntakeAttemptExpiresAtUtc));

        File.GetUnixFileMode(dispatchRoot).ShouldBe(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.GetUnixFileMode(fixture.LedgerRoot).ShouldBe(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Path.GetRelativePath(
                fixture.LedgerRoot,
                DurableOperationLedger.GetDispatchRecordPath(
                    fixture.LedgerRoot,
                    request.RepositoryId,
                    request.OperationId))
            .Split(Path.DirectorySeparatorChar)[0]
            .ShouldBe("dispatch");
    }

    [Fact]
    public void prepared_dispatch_with_non_prepared_operation_is_typed_as_corrupt()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        Unwrap(fixture.Ledger.RegisterDispatch(
            request,
            fixture.Principal,
            registration.DispatchLeaseId,
            registration.DispatchLeaseExpiresAtUtc,
            registration.IntakeAttemptId,
            registration.IntakeAttemptExpiresAtUtc));
        Unwrap(fixture.Ledger.Receive(request, fixture.Principal));
        AdvanceDispatchTo(fixture, request, DispatchIntakePhase.Prepared);

        Error(fixture.Coordinator.RegisterDispatchPlan(registration)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);
    }

    [Theory]
    [InlineData((int)DispatchIntakePhase.Received)]
    [InlineData((int)DispatchIntakePhase.OperationPinned)]
    [InlineData((int)DispatchIntakePhase.Prepared)]
    public void missing_operation_at_a_receipt_backed_dispatch_frontier_is_corrupt_without_mutation(
        int phaseValue)
    {
        using var fixture = new CoordinatorFixture();
        var targetPhase = (DispatchIntakePhase)phaseValue;
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(registration));
        Unwrap(fixture.PlanStore.BeginIntakePin(
            key,
            registration.DispatchLeaseId,
            registration.IntakeAttemptId));
        Unwrap(fixture.Ledger.AdvanceDispatch(
            request.RepositoryId,
            request.OperationId,
            DispatchIntakePhase.DispatchPinned,
            DispatchIntakePhase.IntakePinned,
            "test missing receipt"));
        Unwrap(fixture.Ledger.AdvanceDispatch(
            request.RepositoryId,
            request.OperationId,
            DispatchIntakePhase.IntakePinned,
            DispatchIntakePhase.Received,
            "test missing receipt"));
        if (targetPhase is DispatchIntakePhase.OperationPinned or DispatchIntakePhase.Prepared)
        {
            Unwrap(fixture.PlanStore.PromoteIntakePin(
                key,
                registration.DispatchLeaseId,
                registration.IntakeAttemptId,
                DurableDispatchIntakeCoordinator.FormatOperationReference(request.OperationId)));
            Unwrap(fixture.Ledger.AdvanceDispatch(
                request.RepositoryId,
                request.OperationId,
                DispatchIntakePhase.Received,
                DispatchIntakePhase.OperationPinned,
                "test missing receipt"));
        }
        if (targetPhase == DispatchIntakePhase.Prepared)
        {
            Unwrap(fixture.Ledger.AdvanceDispatch(
                request.RepositoryId,
                request.OperationId,
                DispatchIntakePhase.OperationPinned,
                DispatchIntakePhase.Prepared,
                "test missing receipt"));
        }
        var planPath = Directory.EnumerateFiles(
            fixture.PlanStoreRoot,
            "*.plan",
            SearchOption.AllDirectories).Single();
        var dispatchPath = DurableOperationLedger.GetDispatchRecordPath(
            fixture.LedgerRoot,
            request.RepositoryId,
            request.OperationId);
        var planBefore = File.ReadAllBytes(planPath);
        var dispatchBefore = File.ReadAllBytes(dispatchPath);

        Error(fixture.Coordinator.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);

        File.ReadAllBytes(planPath).ShouldBe(planBefore);
        File.ReadAllBytes(dispatchPath).ShouldBe(dispatchBefore);
        Error(fixture.Ledger.Load(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Missing);
        Unwrap(fixture.PlanStore.LoadRetained(key)).Pins.ShouldHaveSingleItem().Kind.ShouldBe(
            targetPhase == DispatchIntakePhase.Received
                ? ChangePlanPinKind.IntakePending
                : ChangePlanPinKind.OperationLedger);
    }

    [Fact]
    public void operation_frontier_preserves_unsafe_protection_from_its_authoritative_read()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(registration));
        Unwrap(fixture.PlanStore.BeginIntakePin(
            key,
            registration.DispatchLeaseId,
            registration.IntakeAttemptId));
        Unwrap(fixture.Ledger.Receive(request, fixture.Principal));
        Unwrap(fixture.Ledger.AdvanceDispatch(
            request.RepositoryId,
            request.OperationId,
            DispatchIntakePhase.DispatchPinned,
            DispatchIntakePhase.IntakePinned,
            "test operation authority"));
        Unwrap(fixture.Ledger.AdvanceDispatch(
            request.RepositoryId,
            request.OperationId,
            DispatchIntakePhase.IntakePinned,
            DispatchIntakePhase.Received,
            "test operation authority"));
        var coordinator = new DurableDispatchIntakeCoordinator(
            fixture.PlanStore,
            new DurableOperationLedger(
                fixture.LedgerRoot,
                () => InitialNow,
                new FailOnAcquireLedgerProtectionAuthority(fixture.LedgerRoot, failOnAcquire: 2)));

        Error(coordinator.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.UnsafeProtection);
        Unwrap(fixture.PlanStore.LoadRetained(key)).Pins.ShouldHaveSingleItem().Kind
            .ShouldBe(ChangePlanPinKind.IntakePending);
    }

    [Fact]
    public void received_dispatch_with_a_rejected_operation_is_corrupt_without_pin_promotion()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(registration));
        Unwrap(fixture.PlanStore.BeginIntakePin(
            key,
            registration.DispatchLeaseId,
            registration.IntakeAttemptId));
        Unwrap(fixture.Ledger.Receive(request, fixture.Principal));
        Unwrap(fixture.Ledger.AppendTransition(
            request.RepositoryId,
            request.OperationId,
            OperationLifecycleState.Received,
            OperationLifecycleState.RejectedPlanUnavailable,
            "test contradictory lifecycle"));
        Unwrap(fixture.Ledger.AdvanceDispatch(
            request.RepositoryId,
            request.OperationId,
            DispatchIntakePhase.DispatchPinned,
            DispatchIntakePhase.IntakePinned,
            "test contradictory lifecycle"));
        Unwrap(fixture.Ledger.AdvanceDispatch(
            request.RepositoryId,
            request.OperationId,
            DispatchIntakePhase.IntakePinned,
            DispatchIntakePhase.Received,
            "test contradictory lifecycle"));

        Error(fixture.Coordinator.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);
        Unwrap(fixture.PlanStore.LoadRetained(key)).Pins.ShouldHaveSingleItem().Kind
            .ShouldBe(ChangePlanPinKind.IntakePending);
        Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId))
            .CurrentPhase.ShouldBe(DispatchIntakePhase.Received);
    }

    [Theory]
    [InlineData((int)DispatchIntakePhase.Received, "request")]
    [InlineData((int)DispatchIntakePhase.Received, "principal")]
    [InlineData((int)DispatchIntakePhase.Prepared, "request")]
    [InlineData((int)DispatchIntakePhase.Prepared, "principal")]
    [InlineData((int)DispatchIntakePhase.RejectedPlanUnavailable, "request")]
    [InlineData((int)DispatchIntakePhase.RejectedPlanUnavailable, "principal")]
    public void receipt_backed_dispatch_rejects_cross_record_binding_mismatch(
        int phaseValue,
        string mismatch)
    {
        using var fixture = new CoordinatorFixture();
        var targetPhase = (DispatchIntakePhase)phaseValue;
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(registration));
        Unwrap(fixture.PlanStore.BeginIntakePin(
            key,
            registration.DispatchLeaseId,
            registration.IntakeAttemptId));
        var receiptRequest = mismatch == "request"
            ? fixture.CreateRequest(
                fixture.CreateEnvelope(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")),
                request.OperationId)
            : request;
        var receiptPrincipal = mismatch == "principal"
            ? new AuthenticatedPrincipalId("principal-002")
            : fixture.Principal;
        Unwrap(fixture.Ledger.Receive(receiptRequest, receiptPrincipal));
        Unwrap(fixture.Ledger.AdvanceDispatch(
            request.RepositoryId,
            request.OperationId,
            DispatchIntakePhase.DispatchPinned,
            DispatchIntakePhase.IntakePinned,
            "test cross-record binding"));
        Unwrap(fixture.Ledger.AdvanceDispatch(
            request.RepositoryId,
            request.OperationId,
            DispatchIntakePhase.IntakePinned,
            DispatchIntakePhase.Received,
            "test cross-record binding"));
        if (targetPhase == DispatchIntakePhase.Prepared)
        {
            Unwrap(fixture.Ledger.AppendTransition(
                request.RepositoryId,
                request.OperationId,
                OperationLifecycleState.Received,
                OperationLifecycleState.Prepared,
                "test cross-record binding"));
            Unwrap(fixture.Ledger.AdvanceDispatch(
                request.RepositoryId,
                request.OperationId,
                DispatchIntakePhase.Received,
                DispatchIntakePhase.OperationPinned,
                "test cross-record binding"));
            Unwrap(fixture.Ledger.AdvanceDispatch(
                request.RepositoryId,
                request.OperationId,
                DispatchIntakePhase.OperationPinned,
                DispatchIntakePhase.Prepared,
                "test cross-record binding"));
        }
        else if (targetPhase == DispatchIntakePhase.RejectedPlanUnavailable)
        {
            Unwrap(fixture.Ledger.AppendTransition(
                request.RepositoryId,
                request.OperationId,
                OperationLifecycleState.Received,
                OperationLifecycleState.RejectedPlanUnavailable,
                "test cross-record binding"));
            Unwrap(fixture.Ledger.AdvanceDispatch(
                request.RepositoryId,
                request.OperationId,
                DispatchIntakePhase.Received,
                DispatchIntakePhase.RejectedPlanUnavailable,
                "test cross-record binding"));
        }
        var planPath = Directory.EnumerateFiles(
            fixture.PlanStoreRoot,
            "*.plan",
            SearchOption.AllDirectories).Single();
        var dispatchPath = DurableOperationLedger.GetDispatchRecordPath(
            fixture.LedgerRoot,
            request.RepositoryId,
            request.OperationId);
        var operationPath = DurableOperationLedger.GetRecordPath(
            fixture.LedgerRoot,
            request.RepositoryId,
            request.OperationId);
        var planBefore = File.ReadAllBytes(planPath);
        var dispatchBefore = File.ReadAllBytes(dispatchPath);
        var operationBefore = File.ReadAllBytes(operationPath);

        Error(fixture.Coordinator.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);

        File.ReadAllBytes(planPath).ShouldBe(planBefore);
        File.ReadAllBytes(dispatchPath).ShouldBe(dispatchBefore);
        File.ReadAllBytes(operationPath).ShouldBe(operationBefore);
    }

    [Fact]
    public void rejection_revalidates_receipt_binding_before_mutating_its_lifecycle()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var mismatchedRequest = fixture.CreateRequest(
            fixture.CreateEnvelope(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")),
            request.OperationId);
        Unwrap(fixture.Ledger.Receive(mismatchedRequest, fixture.Principal));
        var operationPath = DurableOperationLedger.GetRecordPath(
            fixture.LedgerRoot,
            request.RepositoryId,
            request.OperationId);
        var mismatchedReceipt = File.ReadAllBytes(operationPath);
        File.Delete(operationPath);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        File.Delete(Directory.EnumerateFiles(
            fixture.PlanStoreRoot,
            "*.plan",
            SearchOption.AllDirectories).Single());
        var coordinator = fixture.CreateCoordinator(new DurableDispatchIntakeHooks(boundary =>
        {
            if (boundary == DispatchIntakeBoundary.ReceiptCommitted)
                File.WriteAllBytes(operationPath, mismatchedReceipt);
        }));

        Error(coordinator.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);
        Unwrap(fixture.Ledger.Load(request.RepositoryId, request.OperationId)).CurrentState
            .ShouldBe(OperationLifecycleState.Received);
    }

    [Fact]
    public void rejected_dispatch_that_passed_received_requires_its_operation_receipt()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        Unwrap(fixture.Ledger.RegisterDispatch(
            request,
            fixture.Principal,
            registration.DispatchLeaseId,
            registration.DispatchLeaseExpiresAtUtc,
            registration.IntakeAttemptId,
            registration.IntakeAttemptExpiresAtUtc));
        AdvanceDispatchTo(fixture, request, DispatchIntakePhase.Received);
        Unwrap(fixture.Ledger.AdvanceDispatch(
            request.RepositoryId,
            request.OperationId,
            DispatchIntakePhase.Received,
            DispatchIntakePhase.RejectedPlanUnavailable,
            "test rejection after missing receipt"));
        var dispatchPath = DurableOperationLedger.GetDispatchRecordPath(
            fixture.LedgerRoot,
            request.RepositoryId,
            request.OperationId);
        var dispatchBefore = File.ReadAllBytes(dispatchPath);

        Error(fixture.Coordinator.RegisterDispatchPlan(registration)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);

        File.ReadAllBytes(dispatchPath).ShouldBe(dispatchBefore);
        Error(fixture.Ledger.Load(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Missing);
    }

    [Fact]
    public void rejected_dispatch_from_dispatch_pinned_requires_its_rejected_operation_receipt()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        Unwrap(fixture.Ledger.RegisterDispatch(
            request,
            fixture.Principal,
            registration.DispatchLeaseId,
            registration.DispatchLeaseExpiresAtUtc,
            registration.IntakeAttemptId,
            registration.IntakeAttemptExpiresAtUtc));
        AdvanceDispatchTo(fixture, request, DispatchIntakePhase.DispatchPinned);
        Unwrap(fixture.Ledger.AdvanceDispatch(
            request.RepositoryId,
            request.OperationId,
            DispatchIntakePhase.DispatchPinned,
            DispatchIntakePhase.RejectedPlanUnavailable,
            "test receipt-backed rejection"));

        Error(fixture.Coordinator.RegisterDispatchPlan(registration)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);
    }

    [Fact]
    public void rejected_dispatch_with_non_rejected_operation_is_typed_as_corrupt()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        Unwrap(fixture.Ledger.RegisterDispatch(
            request,
            fixture.Principal,
            registration.DispatchLeaseId,
            registration.DispatchLeaseExpiresAtUtc,
            registration.IntakeAttemptId,
            registration.IntakeAttemptExpiresAtUtc));
        Unwrap(fixture.Ledger.Receive(request, fixture.Principal));
        Unwrap(fixture.Ledger.AdvanceDispatch(
            request.RepositoryId,
            request.OperationId,
            DispatchIntakePhase.Registered,
            DispatchIntakePhase.RejectedPlanUnavailable,
            "test rejection"));

        Error(fixture.Coordinator.RegisterDispatchPlan(registration)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);
    }

    [Fact]
    public void impossible_durable_dispatch_expiry_is_corrupt_instead_of_reaching_plan_store_code()
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        var path = DurableOperationLedger.GetDispatchRecordPath(
            fixture.LedgerRoot,
            request.RepositoryId,
            request.OperationId);
        var record = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        record["dispatchLeaseExpiresAtUtc"] = JsonValue.Create(InitialNow);
        record["intakeAttemptExpiresAtUtc"] = JsonValue.Create(InitialNow);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(record.ToJsonString()));

        Error(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
        Error(fixture.Coordinator.ReceiveAndPrepare(request, fixture.Principal)).Kind
            .ShouldBe(DurableDispatchIntakeErrorKind.CorruptState);
    }

    [Fact]
    public void equal_dispatch_and_attempt_expiry_is_valid_and_restart_readable()
    {
        using var fixture = new CoordinatorFixture();
        var request = fixture.CreateRequest(fixture.CreateEnvelope());
        var expiry = InitialNow + TimeSpan.FromMinutes(10);

        Unwrap(fixture.Ledger.RegisterDispatch(
            request,
            fixture.Principal,
            "dispatch",
            expiry,
            "attempt",
            expiry));

        Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId))
            .IntakeAttemptExpiresAtUtc.ShouldBe(expiry);
    }

    [Theory]
    [InlineData("attempt-at-registration")]
    [InlineData("attempt-after-dispatch")]
    [InlineData("non-utc-dispatch")]
    public void impossible_durable_dispatch_lease_boundaries_are_corrupt(string mutation)
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        var path = DurableOperationLedger.GetDispatchRecordPath(
            fixture.LedgerRoot,
            request.RepositoryId,
            request.OperationId);
        var record = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        switch (mutation)
        {
            case "attempt-at-registration":
                record["intakeAttemptExpiresAtUtc"] = JsonValue.Create(InitialNow);
                break;
            case "attempt-after-dispatch":
                record["intakeAttemptExpiresAtUtc"] = JsonValue.Create(
                    InitialNow + TimeSpan.FromMinutes(11));
                break;
            case "non-utc-dispatch":
                record["dispatchLeaseExpiresAtUtc"] = JsonValue.Create(
                    (InitialNow + TimeSpan.FromMinutes(10)).ToOffset(TimeSpan.FromHours(1)));
                break;
            default:
                throw new ShouldAssertException($"Unknown lease mutation '{mutation}'.");
        }
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(record.ToJsonString()));

        Error(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
    }

    [Theory]
    [InlineData("dispatchLeaseId")]
    [InlineData("intakeAttemptId")]
    public void overlong_durable_coordination_reference_is_corrupt(string propertyName)
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        var path = DurableOperationLedger.GetDispatchRecordPath(
            fixture.LedgerRoot,
            request.RepositoryId,
            request.OperationId);
        var record = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        record[propertyName] = new string('x', 257);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(record.ToJsonString()));

        Error(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("first-sequence")]
    [InlineData("first-phase")]
    [InlineData("last-phase")]
    [InlineData("later-sequence")]
    [InlineData("reversed-time")]
    [InlineData("invalid-edge")]
    public void impossible_durable_dispatch_history_is_corrupt(string mutation)
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        var path = DurableOperationLedger.GetDispatchRecordPath(
            fixture.LedgerRoot,
            request.RepositoryId,
            request.OperationId);
        var record = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        var transitions = record["transitions"]!.AsArray();
        switch (mutation)
        {
            case "empty":
                record["transitions"] = new JsonArray();
                break;
            case "first-sequence":
                transitions[0]!["sequence"] = 2;
                break;
            case "first-phase":
                transitions[0]!["phase"] = (int)DispatchIntakePhase.DispatchPinned;
                break;
            case "last-phase":
                record["currentPhase"] = (int)DispatchIntakePhase.Registered;
                break;
            case "later-sequence":
                transitions[1]!["sequence"] = 3;
                break;
            case "reversed-time":
                transitions[1]!["committedAtUtc"] = JsonValue.Create(InitialNow - TimeSpan.FromTicks(1));
                break;
            case "invalid-edge":
                transitions[1]!["phase"] = (int)DispatchIntakePhase.Prepared;
                record["currentPhase"] = (int)DispatchIntakePhase.Prepared;
                break;
            default:
                throw new ShouldAssertException($"Unknown history mutation '{mutation}'.");
        }
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(record.ToJsonString()));

        Error(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("operation")]
    [InlineData("null-transition")]
    public void dispatch_record_address_and_entries_are_exact(string mutation)
    {
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        Unwrap(fixture.Coordinator.RegisterDispatchPlan(fixture.CreateRegistration(request)));
        var path = DurableOperationLedger.GetDispatchRecordPath(
            fixture.LedgerRoot,
            request.RepositoryId,
            request.OperationId);
        var record = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        switch (mutation)
        {
            case "repository":
                record["repositoryId"] = "different-repository";
                break;
            case "operation":
                record["operationNonce"] = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
                break;
            case "null-transition":
                record["transitions"]!.AsArray().Add(null);
                break;
            default:
                throw new ShouldAssertException($"Unknown record mutation '{mutation}'.");
        }
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(record.ToJsonString()));

        Error(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId)).Kind
            .ShouldBe(OperationLedgerErrorKind.Corrupt);
    }

    public static IEnumerable<object[]> DurableBoundaries =>
        Enum.GetValues<DispatchIntakeBoundary>().Select(boundary => new object[] { (int)boundary });

    [Theory]
    [MemberData(nameof(DurableBoundaries))]
    public void restart_reconciles_every_dispatch_pin_receipt_and_prepared_boundary(
        int boundaryValue)
    {
        var boundary = (DispatchIntakeBoundary)boundaryValue;
        using var fixture = new CoordinatorFixture();
        var envelope = fixture.CreateEnvelope();
        var key = Unwrap(fixture.PlanStore.Persist(envelope));
        var request = fixture.CreateRequest(envelope);
        var registration = fixture.CreateRegistration(request);
        var failed = false;
        var interrupted = fixture.CreateCoordinator(new DurableDispatchIntakeHooks(current =>
        {
            if (!failed && current == boundary)
            {
                failed = true;
                throw new InvalidOperationException($"Simulated termination after {current}.");
            }
        }));

        Should.Throw<InvalidOperationException>(() =>
        {
            Unwrap(interrupted.RegisterDispatchPlan(registration));
            Unwrap(interrupted.ReceiveAndPrepare(request, fixture.Principal));
        }).Message.ShouldContain(boundary.ToString());

        var restarted = fixture.CreateCoordinator();
        Unwrap(restarted.RegisterDispatchPlan(registration)).Disposition.ShouldBeOneOf(
            DurableDispatchIntakeDisposition.Ready,
            DurableDispatchIntakeDisposition.Replay);
        var recovered = Unwrap(restarted.ReceiveAndPrepare(request, fixture.Principal));
        recovered.Disposition.ShouldBeOneOf(
            DurableDispatchIntakeDisposition.Prepared,
            DurableDispatchIntakeDisposition.Replay);
        recovered.Operation!.CurrentState.ShouldBe(OperationLifecycleState.Prepared);
        Unwrap(fixture.Ledger.LoadDispatch(request.RepositoryId, request.OperationId))
            .CurrentPhase.ShouldBe(DispatchIntakePhase.Prepared);
        Unwrap(fixture.PlanStore.Load(key)).Pins.ShouldHaveSingleItem().Kind
            .ShouldBe(ChangePlanPinKind.OperationLedger);
        File.Exists(Path.Combine(fixture.RepositoryRoot, "src", "Added.cs")).ShouldBeFalse();
    }

    private static T Unwrap<T, TError>(Result<T, TError> result) => result.Match(
        value => value,
        error => throw new ShouldAssertException($"Expected success, got {error}."));

    private static TError Error<T, TError>(Result<T, TError> result) => result.Match(
        _ => throw new ShouldAssertException("Expected an error result."),
        error => error);

    private static RewriteAndFailAfterLedgerReplaceDurability CorruptAfterLedgerReplace(
        int failOnReplacement) =>
        new RewriteAndFailAfterLedgerReplaceDurability(
            failOnReplacement,
            record => record["schemaVersion"] = 0);

    private static void AdvanceDispatchTo(
        CoordinatorFixture fixture,
        CanonicalApplyRequest request,
        DispatchIntakePhase target)
    {
        var phase = DispatchIntakePhase.Registered;
        foreach (var next in new[]
                 {
                     DispatchIntakePhase.DispatchPinned,
                     DispatchIntakePhase.IntakePinned,
                     DispatchIntakePhase.Received,
                     DispatchIntakePhase.OperationPinned,
                     DispatchIntakePhase.Prepared
                 })
        {
            Unwrap(fixture.Ledger.AdvanceDispatch(
                request.RepositoryId,
                request.OperationId,
                phase,
                next,
                "test frontier"));
            phase = next;
            if (phase == target)
                return;
        }
        throw new ShouldAssertException($"Unsupported dispatch test target '{target}'.");
    }

    private static void RewriteRejectionFrontier(JsonObject record, string target)
    {
        var transitions = record["transitions"]!.AsArray();
        if (target == "operation")
        {
            record["currentState"] = (int)OperationLifecycleState.Prepared;
            transitions[^1]!["state"] = (int)OperationLifecycleState.Prepared;
            return;
        }
        if (target == "dispatch")
        {
            record["currentPhase"] = (int)DispatchIntakePhase.IntakePinned;
            transitions[^1]!["phase"] = (int)DispatchIntakePhase.IntakePinned;
            return;
        }
        throw new ShouldAssertException($"Unsupported rejection rewrite target '{target}'.");
    }

    private sealed class CoordinatorFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"rt-dispatch-intake-tests-{Guid.NewGuid():N}");
        private readonly TestProtectionAuthority _protection;

        internal CoordinatorFixture()
        {
            RepositoryRoot = Path.Combine(_root, "repository");
            PlanStoreRoot = Path.Combine(_root, "plan-store");
            LedgerRoot = Path.Combine(_root, "ledger");
            ControlRoot = Path.Combine(_root, "control");
            Directory.CreateDirectory(Path.Combine(RepositoryRoot, "src"));
            Directory.CreateDirectory(ControlRoot);
            _protection = new TestProtectionAuthority(PlanStoreRoot, LedgerRoot);
            PlanStore = new DurableChangePlanStore(
                PlanStoreRoot,
                utcNow: () => InitialNow,
                protectionAuthority: _protection);
            Ledger = new DurableOperationLedger(
                LedgerRoot,
                utcNow: () => InitialNow,
                protectionAuthority: _protection);
            Coordinator = new DurableDispatchIntakeCoordinator(PlanStore, Ledger);
        }

        internal string RepositoryRoot { get; }
        internal string PlanStoreRoot { get; }
        internal string LedgerRoot { get; }
        internal string ControlRoot { get; }
        internal AuthenticatedPrincipalId Principal { get; } = new("principal-001");
        internal DurableChangePlanStore PlanStore { get; }
        internal DurableOperationLedger Ledger { get; }
        internal DurableDispatchIntakeCoordinator Coordinator { get; }

        internal DurableDispatchIntakeCoordinator CreateCoordinator(
            DurableDispatchIntakeHooks? hooks = null,
            Func<DateTimeOffset>? planUtcNow = null,
            Func<DateTimeOffset>? ledgerUtcNow = null,
            IChangePlanStoreDurability? planDurability = null,
            IOperationLedgerDurability? ledgerDurability = null,
            ChangePlanStoreOptions? planOptions = null) => new(
            new DurableChangePlanStore(
                PlanStoreRoot,
                options: planOptions,
                utcNow: planUtcNow ?? (() => InitialNow),
                protectionAuthority: _protection,
                durability: planDurability),
            new DurableOperationLedger(
                LedgerRoot,
                utcNow: ledgerUtcNow ?? (() => InitialNow),
                protectionAuthority: _protection,
                durability: ledgerDurability),
            hooks);

        internal DurableOperationLedger CreateLedger(Func<DateTimeOffset> utcNow) => new(
            LedgerRoot,
            utcNow,
            _protection);

        internal DispatchPlanRegistration CreateRegistration(
            CanonicalApplyRequest request,
            string dispatchLeaseId = "dispatch-1",
            string intakeAttemptId = "attempt-1") => new(
            request,
            Principal,
            dispatchLeaseId,
            InitialNow + TimeSpan.FromMinutes(10),
            intakeAttemptId,
            InitialNow + TimeSpan.FromMinutes(5));

        internal CanonicalApplyRequest CreateRequest(
            ChangePlanEnvelope envelope,
            OperationId? operationId = null)
        {
            var claims = CreateClaims(envelope);
            return CanonicalApplyRequest.Create(
                envelope.Payload.Repository.RepositoryId,
                operationId ?? new OperationId(42, Guid.Parse("99999999-8888-7777-6666-555555555555")),
                envelope.Payload.PlanId,
                envelope.PlanHash,
                envelope.Payload.Repository.BaseRevision,
                claims);
        }

        internal OperationEpochClaims CreateClaims(ChangePlanEnvelope envelope) => new(
            envelope.Payload.Repository.RepositoryId,
            Principal.Value,
            42,
            8,
            1024 * 1024,
            InitialNow + TimeSpan.FromHours(1));

        internal ChangePlanEnvelope CreateEnvelope(
            Guid? planId = null,
            DateTimeOffset? expiresAtUtc = null)
        {
            var sourcePath = "src/Added.cs";
            var sourceResolvedPath = Path.Combine(RepositoryRoot, sourcePath);
            var content = Encoding.UTF8.GetBytes("class Added;\n");
            var after = new ChangePlanPathState(
                exists: true,
                ChangePlanPathKind.RegularFile,
                Hash(content),
                platformFileIdentity: null,
                linkCount: 1,
                knownHardLinkAliases: [],
                encodingName: "utf-8",
                hasByteOrderMark: false,
                newline: "lf",
                fileMode: "0644",
                executable: false,
                readOnly: false,
                symlinkTarget: null);
            var directoryState = new ChangePlanPathState(
                exists: true,
                ChangePlanPathKind.Directory,
                contentHash: null,
                platformFileIdentity: "device:test;inode:src",
                linkCount: 2,
                knownHardLinkAliases: [],
                encodingName: null,
                hasByteOrderMark: null,
                newline: null,
                fileMode: "0755",
                executable: true,
                readOnly: false,
                symlinkTarget: null);
            var guard = new ChangePlanGuardRequirement(
                sourcePath,
                ChangePlanGuardKind.ExternalWriteGuard,
                "guard-create-v1");
            var payload = new ChangePlanPayload(
                planId ?? Guid.Parse("11111111-2222-3333-4444-555555555555"),
                new ChangePlanRepository(
                    "repository-001",
                    RepositoryRoot,
                    RepositoryRoot,
                    "device:test;inode:repository",
                    new string('a', 40),
                    new string('b', 64)),
                InitialNow - TimeSpan.FromMinutes(1),
                expiresAtUtc ?? InitialNow + TimeSpan.FromMinutes(20),
                [new ChangePlanOperation(1, "create", sourcePath, [])],
                [
                    new ChangePlanDependency(
                        "src",
                        Path.Combine(RepositoryRoot, "src"),
                        ChangePlanDependencyKind.DirectoryEntry,
                        directoryState,
                        new ChangePlanGuardRequirement(
                            "src",
                            ChangePlanGuardKind.ExternalWriteGuard,
                            "guard-directory-v1"))
                ],
                [
                    new ChangePlanEffect(
                        1,
                        ChangePlanEffectKind.Create,
                        ChangePlanArtifactKind.RoslynDocument,
                        sourcePath,
                        sourceResolvedPath,
                        destinationPath: null,
                        destinationResolvedPath: null,
                        ChangePlanPathState.Missing,
                        after,
                        destinationBefore: null,
                        destinationAfter: null,
                        originalContent: null,
                        replacementContent: content,
                        [guard])
                ],
                diagnostics: [],
                assumptions: [],
                new ChangePlanConfidence("Proof", "Complete workspace", ["All inputs loaded"]),
                limitations: [],
                new ChangePlanWorkspace(
                    WorkspaceCompletenessState.Complete,
                    Path.Combine(RepositoryRoot, "RoslynTools.slnx"),
                    "Debug",
                    ["net10.0"],
                    ".NET SDK 10.0.100",
                    "Microsoft.CodeAnalysis 5.0.0",
                    "InProcess",
                    "Trusted"),
                [new ChangePlanVerificationAction(1, "build", "dotnet build", "success", required: true)],
                new ChangePlanCoordination(
                    ChangePlanCoordinationDomain.ExclusivePrincipal,
                    ChangePlanCoordinationMode.ExternalWriteGuards,
                    "one private writer",
                    ControlRoot,
                    ControlRoot,
                    "device:test;inode:control",
                    "device:test",
                    Path.Combine(ControlRoot, "staging"),
                    Path.Combine(ControlRoot, "staging"),
                    "device:test;inode:staging",
                    Path.Combine(ControlRoot, "backup"),
                    Path.Combine(ControlRoot, "backup"),
                    "device:test;inode:backup"),
                new ChangePlanPathPolicy(
                    ChangePlanCaseSensitivity.Sensitive,
                    ChangePlanLongPathPolicy.Reject,
                    ChangePlanReadOnlyPolicy.Reject,
                    ChangePlanSymlinkPolicy.Reject,
                    ChangePlanHardLinkPolicy.Reject),
                ChangePlanCleanupPolicy.RetainUntilVerifiedTerminal,
                ChangePlanRecoveryPolicy.RequireDurableJournal);
            return ChangePlanEnvelope.Create(payload);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private static string Hash(byte[] content) =>
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    private sealed class TestProtectionAuthority(string expectedPlanRoot, string expectedLedgerRoot)
        : IChangePlanStoreProtectionAuthority, IOperationLedgerProtectionAuthority
    {
        public ChangePlanStoreError? AcquireForPersistence(
            string storeRootPath,
            ChangePlanRepository repository,
            out IChangePlanStoreProtectionLease? lease)
        {
            storeRootPath.ShouldBe(expectedPlanRoot);
            lease = new ProtectionLease();
            return null;
        }

        public ChangePlanStoreError? AcquireExisting(
            string storeRootPath,
            out IChangePlanStoreProtectionLease? lease)
        {
            storeRootPath.ShouldBe(expectedPlanRoot);
            lease = new ProtectionLease();
            return null;
        }

        public OperationLedgerError? Acquire(
            string ledgerRootPath,
            out IOperationLedgerProtectionLease? lease)
        {
            ledgerRootPath.ShouldBe(expectedLedgerRoot);
            lease = new ProtectionLease();
            return null;
        }

        public OperationLedgerError? AcquireExisting(
            string ledgerRootPath,
            out IOperationLedgerProtectionLease? lease) =>
            Acquire(ledgerRootPath, out lease);
    }

    private sealed class SwitchableLedgerProtectionAuthority(string expectedLedgerRoot)
        : IOperationLedgerProtectionAuthority
    {
        private bool _denied;

        internal void Deny() => _denied = true;

        public OperationLedgerError? AcquireExisting(
            string ledgerRootPath,
            out IOperationLedgerProtectionLease? lease) =>
            Acquire(ledgerRootPath, out lease);

        public OperationLedgerError? Acquire(
            string ledgerRootPath,
            out IOperationLedgerProtectionLease? lease)
        {
            ledgerRootPath.ShouldBe(expectedLedgerRoot);
            if (_denied)
            {
                lease = null;
                return new OperationLedgerError(
                    OperationLedgerErrorKind.UnsafeProtection,
                    "Injected protection-authority loss.");
            }
            lease = new ProtectionLease();
            return null;
        }
    }

    private sealed class FailOnAcquireLedgerProtectionAuthority(
        string expectedLedgerRoot,
        int failOnAcquire) : IOperationLedgerProtectionAuthority
    {
        private int _acquisitionCount;

        public OperationLedgerError? AcquireExisting(
            string ledgerRootPath,
            out IOperationLedgerProtectionLease? lease) =>
            Acquire(ledgerRootPath, out lease);

        public OperationLedgerError? Acquire(
            string ledgerRootPath,
            out IOperationLedgerProtectionLease? lease)
        {
            ledgerRootPath.ShouldBe(expectedLedgerRoot);
            if (Interlocked.Increment(ref _acquisitionCount) == failOnAcquire)
            {
                lease = null;
                return new OperationLedgerError(
                    OperationLedgerErrorKind.UnsafeProtection,
                    "Injected protection-authority loss.");
            }
            lease = new ProtectionLease();
            return null;
        }
    }

    private sealed class ProtectionLease :
        IChangePlanStoreProtectionLease,
        IOperationLedgerProtectionLease
    {
        public void Dispose()
        {
        }
    }

    private sealed class FaultAfterEveryReplaceDurability : IChangePlanStoreDurability
    {
        private bool _replacementPendingFlush;

        internal int InjectedFailureCount { get; private set; }

        public void FlushFileMetadata(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            stream.Flush(flushToDisk: true);
        }

        public void ReplaceFile(string temporaryPath, string recordPath)
        {
            File.Move(temporaryPath, recordPath, overwrite: true);
            _replacementPendingFlush = true;
        }

        public void FlushDirectoryMetadata(string directory)
        {
            if (!_replacementPendingFlush)
                return;
            _replacementPendingFlush = false;
            InjectedFailureCount++;
            throw new IOException("Injected directory-flush failure after record replacement.");
        }
    }

    private sealed class FailBeforePlanReplaceDurability(
        int failOnReplacement,
        Action<string, string> beforeFailure) : IChangePlanStoreDurability
    {
        private int _replacementCount;

        public void FlushFileMetadata(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            stream.Flush(flushToDisk: true);
        }

        public void ReplaceFile(string temporaryPath, string recordPath)
        {
            if (Interlocked.Increment(ref _replacementCount) == failOnReplacement)
            {
                beforeFailure(temporaryPath, recordPath);
                throw new IOException("Injected failure before plan record replacement.");
            }
            File.Move(temporaryPath, recordPath, overwrite: true);
        }

        public void FlushDirectoryMetadata(string directory)
        {
        }
    }

    private sealed class FaultAfterEveryLedgerReplaceDurability : IOperationLedgerDurability
    {
        private bool _replacementPendingFlush;

        internal int InjectedFailureCount { get; private set; }

        public void FlushFileMetadata(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            stream.Flush(flushToDisk: true);
        }

        public void ReplaceFile(string temporaryPath, string recordPath)
        {
            File.Move(temporaryPath, recordPath, overwrite: true);
            _replacementPendingFlush = true;
        }

        public void FlushDirectoryMetadata(string directory)
        {
            if (!_replacementPendingFlush)
                return;
            _replacementPendingFlush = false;
            InjectedFailureCount++;
            throw new IOException("Injected ledger directory-flush failure after record replacement.");
        }
    }

    private sealed class FailAfterLedgerReplaceDurability(
        int failOnReplacement,
        Action<string> afterReplacement) : IOperationLedgerDurability
    {
        private int _replacementCount;

        public void FlushFileMetadata(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            stream.Flush(flushToDisk: true);
        }

        public void ReplaceFile(string temporaryPath, string recordPath)
        {
            File.Move(temporaryPath, recordPath, overwrite: true);
            if (Interlocked.Increment(ref _replacementCount) != failOnReplacement)
                return;
            afterReplacement(recordPath);
            throw new IOException("Injected failure after ledger replacement.");
        }

        public void FlushDirectoryMetadata(string directory)
        {
        }
    }

    private sealed class FailBeforeLedgerReplaceDurability(int failOnReplacement)
        : IOperationLedgerDurability
    {
        private int _replacementCount;

        public void FlushFileMetadata(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            stream.Flush(flushToDisk: true);
        }

        public void ReplaceFile(string temporaryPath, string recordPath)
        {
            if (Interlocked.Increment(ref _replacementCount) == failOnReplacement)
                throw new IOException("Injected failure before ledger record replacement.");
            File.Move(temporaryPath, recordPath, overwrite: true);
        }

        public void FlushDirectoryMetadata(string directory)
        {
        }
    }

    private sealed class RewriteAndFailAfterLedgerReplaceDurability(
        int failOnReplacement,
        Action<JsonObject> rewrite) : IOperationLedgerDurability
    {
        private int _replacementCount;

        public void FlushFileMetadata(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            stream.Flush(flushToDisk: true);
        }

        public void ReplaceFile(string temporaryPath, string recordPath)
        {
            if (Interlocked.Increment(ref _replacementCount) == failOnReplacement)
            {
                var record = JsonNode.Parse(File.ReadAllBytes(temporaryPath))!.AsObject();
                rewrite(record);
                File.WriteAllText(temporaryPath, record.ToJsonString());
                File.Move(temporaryPath, recordPath, overwrite: true);
                throw new IOException("Injected failure after a contradictory ledger replacement.");
            }
            File.Move(temporaryPath, recordPath, overwrite: true);
        }

        public void FlushDirectoryMetadata(string directory)
        {
        }
    }

}
