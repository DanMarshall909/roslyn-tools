using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Cli.Suggest;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class DurablePrepareOperationTests
{
    private static readonly DateTimeOffset InitialNow =
        new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    public sealed class HostFixture : IDisposable
    {
        private readonly PrepareFixture _fixture;

        public HostFixture(DateTimeOffset now)
        {
            Now = now.ToUniversalTime();
            _fixture = new PrepareFixture(Now);
            RepositoryRoot = _fixture.RepositoryRoot;
            PlanId = _fixture.Envelope.Payload.PlanId;
            PlanHash = _fixture.Envelope.PlanHash;
            ExpectedRevision = _fixture.Envelope.Payload.Repository.BaseRevision;
            ProtectedControlRoot = Path.Combine(
                Path.GetDirectoryName(RepositoryRoot)!,
                "host-protected-control");
        }

        public DateTimeOffset Now { get; }

        public string RepositoryRoot { get; }

        public Guid PlanId { get; }

        public string PlanHash { get; }

        public string ExpectedRevision { get; }

        public string ProtectedControlRoot { get; }

        public string SnapshotRepository() =>
            DurablePrepareOperationTests.SnapshotRepository(RepositoryRoot);

        public void RegisterProductionComposition(IServiceCollection services) =>
            DurablePrepareOperationTests.RegisterProductionComposition(
                services,
                ProtectedControlRoot,
                Now);

        public void SeedPlan(IServiceProvider provider)
        {
            var persisted = provider.GetRequiredService<DurableChangePlanStore>()
                .Persist(_fixture.Envelope);
            persisted.IsOk.ShouldBeTrue();
        }

        public void Dispose() => _fixture.Dispose();
    }

    public static IEnumerable<object[]> PersistenceBoundaries =>
        Enum.GetValues<DispatchIntakeBoundary>()
            .Select(boundary => new object[] { (int)boundary });

    [Fact]
    public void constructor_rejects_each_missing_collaborator()
    {
        using var fixture = new PrepareFixture();

        Should.Throw<ArgumentNullException>(() => new DurablePrepareOperation(
            null!,
            fixture.Coordinator,
            new TestPrincipalProvider("principal-001"))).ParamName.ShouldBe("planStore");
        Should.Throw<ArgumentNullException>(() => new DurablePrepareOperation(
            fixture.PlanStore,
            null!,
            new TestPrincipalProvider("principal-001"))).ParamName.ShouldBe("coordinator");
        Should.Throw<ArgumentNullException>(() => new DurablePrepareOperation(
            fixture.PlanStore,
            fixture.Coordinator,
            null!)).ParamName.ShouldBe("operationContextProvider");
    }

    [Fact]
    public void execute_rejects_a_missing_request_and_pre_cancelled_call()
    {
        using var fixture = new PrepareFixture();

        Should.Throw<ArgumentNullException>(() => fixture.Operation.ExecuteAsync(null!))
            .ParamName.ShouldBe("request");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Should.Throw<OperationCanceledException>(() =>
            fixture.Operation.ExecuteAsync(fixture.CreateRequest(), cancellation.Token));
    }

    [Fact]
    public async Task exact_plan_reaches_durable_prepared_without_changing_repository_bytes()
    {
        using var fixture = new PrepareFixture();
        var before = fixture.SnapshotRepository();

        var result = await fixture.Operation.ExecuteAsync(fixture.CreateRequest());

        result.IsOk.ShouldBeTrue();
        var prepared = result.Unwrap();
        prepared.Disposition.ShouldBe(DurablePrepareDisposition.Prepared);
        prepared.State.ShouldBe(OperationLifecycleState.Prepared);
        prepared.RepositoryId.ShouldBe("repository-001");
        prepared.PlanId.ShouldBe(fixture.Envelope.Payload.PlanId);
        prepared.PlanHash.ShouldBe(fixture.Envelope.PlanHash);
        prepared.RequestHash.ShouldNotBeNullOrWhiteSpace();
        fixture.SnapshotRepository().ShouldBe(before);

        var operation = fixture.LoadOperation(prepared);
        operation.Transitions.Select(transition => transition.State).ShouldBe([
            OperationLifecycleState.Received,
            OperationLifecycleState.Prepared
        ]);
    }

    [Fact]
    public async Task exact_replay_returns_the_retained_operation_without_history_churn()
    {
        using var fixture = new PrepareFixture();
        var request = fixture.CreateRequest();
        var first = (await fixture.Operation.ExecuteAsync(request)).Unwrap();
        var before = fixture.LoadOperation(first);
        var repositoryBefore = fixture.SnapshotRepository();

        var replay = (await fixture.Operation.ExecuteAsync(request)).Unwrap();

        replay.Disposition.ShouldBe(DurablePrepareDisposition.Replay);
        replay.ShouldSatisfyAllConditions(
            value => value.RepositoryId.ShouldBe(first.RepositoryId),
            value => value.OperationEpoch.ShouldBe(first.OperationEpoch),
            value => value.OperationNonce.ShouldBe(first.OperationNonce),
            value => value.PlanId.ShouldBe(first.PlanId),
            value => value.PlanHash.ShouldBe(first.PlanHash),
            value => value.RequestHash.ShouldBe(first.RequestHash),
            value => value.State.ShouldBe(OperationLifecycleState.Prepared));
        fixture.LoadOperation(replay).Transitions.ToArray().ShouldBe(before.Transitions.ToArray());
        fixture.SnapshotRepository().ShouldBe(repositoryBefore);
    }

    [Fact]
    public async Task conflicting_reuse_returns_typed_error_without_changing_the_canonical_operation()
    {
        using var fixture = new PrepareFixture();
        var first = (await fixture.Operation.ExecuteAsync(fixture.CreateRequest())).Unwrap();
        var before = fixture.LoadOperation(first);
        var repositoryBefore = fixture.SnapshotRepository();
        var planStoreBefore = fixture.SnapshotPlanStore();
        var conflictingEnvelope = fixture.CreateEnvelope(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

        var conflict = await fixture.Operation.ExecuteAsync(
            fixture.CreateRequest(conflictingEnvelope));

        var error = Error(conflict);
        error.Kind.ShouldBe(RtErrorKind.OperationConflict);
        error.Message.ShouldNotBeNullOrWhiteSpace();
        error.Context.ShouldBeNull();
        var retained = fixture.LoadOperation(first);
        retained.ShouldSatisfyAllConditions(
            value => value.RepositoryId.ShouldBe(before.RepositoryId),
            value => value.OperationId.ShouldBe(before.OperationId),
            value => value.RequestIdentity.ShouldBe(before.RequestIdentity),
            value => value.SubmittingPrincipalId.ShouldBe(before.SubmittingPrincipalId),
            value => value.CurrentState.ShouldBe(before.CurrentState),
            value => value.CanonicalRequestBytes.ToArray().ShouldBe(before.CanonicalRequestBytes.ToArray()),
            value => value.Transitions.ToArray().ShouldBe(before.Transitions.ToArray()));
        (await fixture.Operation.ExecuteAsync(fixture.CreateRequest())).Unwrap()
            .Disposition.ShouldBe(DurablePrepareDisposition.Replay);
        fixture.SnapshotRepository().ShouldBe(repositoryBefore);
        fixture.SnapshotPlanStore().ShouldBe(planStoreBefore);
    }

    [Fact]
    public async Task another_principal_gets_one_no_disclosure_denial_for_exact_and_conflicting_requests()
    {
        using var fixture = new PrepareFixture();
        var first = (await fixture.Operation.ExecuteAsync(fixture.CreateRequest())).Unwrap();
        var before = fixture.LoadOperation(first);
        var repositoryBefore = fixture.SnapshotRepository();
        var planStoreBefore = fixture.SnapshotPlanStore();
        var otherPrincipal = fixture.CreateOperation("principal-002");

        var exact = Error(await otherPrincipal.ExecuteAsync(fixture.CreateRequest()));
        var conflicting = Error(await otherPrincipal.ExecuteAsync(fixture.CreateRequest(
            fixture.CreateEnvelope(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")))));

        exact.ShouldBe(conflicting);
        exact.Kind.ShouldBe(RtErrorKind.OperationAccessDenied);
        exact.Message.ShouldNotBeNullOrWhiteSpace();
        exact.Context.ShouldBeNull();
        fixture.LoadOperation(first).Transitions.ToArray().ShouldBe(before.Transitions.ToArray());
        fixture.SnapshotRepository().ShouldBe(repositoryBefore);
        fixture.SnapshotPlanStore().ShouldBe(planStoreBefore);
    }

    [Fact]
    public async Task missing_protection_authority_fails_closed_without_repository_effects()
    {
        using var fixture = new PrepareFixture();
        var before = fixture.SnapshotRepository();
        var planStore = new DurableChangePlanStore(
            Path.Combine(fixture.ChangeRoot, "unprotected-plans"),
            utcNow: () => InitialNow);
        var ledger = new DurableOperationLedger(
            Path.Combine(fixture.ChangeRoot, "unprotected-ledger"),
            utcNow: () => InitialNow);
        var operation = new DurablePrepareOperation(
            planStore,
            new DurableDispatchIntakeCoordinator(planStore, ledger),
            new TestPrincipalProvider("principal-001"));

        var error = Error(await operation.ExecuteAsync(fixture.CreateRequest()));

        error.Kind.ShouldBe(RtErrorKind.UnsafeCoordination);
        fixture.SnapshotRepository().ShouldBe(before);
    }

    [Fact]
    public async Task expired_plan_is_a_typed_unavailable_result_without_repository_effects()
    {
        using var fixture = new PrepareFixture();
        var before = fixture.SnapshotRepository();
        var expired = fixture.CreateEnvelope(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            InitialNow + TimeSpan.FromMinutes(1));
        fixture.Persist(expired);
        fixture.AdvanceTo(InitialNow + TimeSpan.FromMinutes(2));

        var error = Error(await fixture.Operation.ExecuteAsync(fixture.CreateRequest(expired)));

        error.Kind.ShouldBe(RtErrorKind.PlanUnavailable);
        error.Message.ShouldNotBeNullOrWhiteSpace();
        fixture.SnapshotRepository().ShouldBe(before);
    }

    [Fact]
    public async Task stored_plan_revision_mismatch_is_typed_unavailable_without_registration()
    {
        using var fixture = new PrepareFixture();
        var before = fixture.SnapshotRepository();

        var error = Error(await fixture.Operation.ExecuteAsync(fixture.CreateRequest(
            expectedRevision: new string('c', 40))));

        error.Kind.ShouldBe(RtErrorKind.PlanUnavailable);
        error.Message.ShouldNotBeNullOrWhiteSpace();
        Directory.Exists(fixture.LedgerRoot).ShouldBeFalse();
        fixture.SnapshotRepository().ShouldBe(before);
    }

    [Fact]
    public async Task host_context_mismatch_is_denied_before_plan_or_ledger_mutation()
    {
        using var fixture = new PrepareFixture();
        var before = fixture.SnapshotRepository();
        var planStoreBefore = fixture.SnapshotPlanStore();
        var operation = fixture.CreateOperation(new MismatchedContextProvider());

        var error = Error(await operation.ExecuteAsync(fixture.CreateRequest()));

        error.Kind.ShouldBe(RtErrorKind.OperationAccessDenied);
        error.Message.ShouldNotBeNullOrWhiteSpace();
        Directory.Exists(fixture.LedgerRoot).ShouldBeFalse();
        fixture.SnapshotPlanStore().ShouldBe(planStoreBefore);
        fixture.SnapshotRepository().ShouldBe(before);
    }

    [Fact]
    public async Task invalid_dispatch_lease_is_typed_invalid_input_without_repository_effects()
    {
        using var fixture = new PrepareFixture();
        var before = fixture.SnapshotRepository();

        var error = Error(await fixture.Operation.ExecuteAsync(fixture.CreateRequest(
            dispatchLeaseExpiresAtUtc: InitialNow - TimeSpan.FromSeconds(1))));

        error.Kind.ShouldBe(RtErrorKind.InvalidInput);
        fixture.SnapshotRepository().ShouldBe(before);
    }

    [Fact]
    public async Task missing_plan_after_registered_identity_becomes_terminal_no_effect_rejection()
    {
        using var fixture = new PrepareFixture();
        var request = fixture.CreateRequest();
        var before = fixture.SnapshotRepository();
        var interrupted = fixture.CreateFreshOperation(
            "principal-001",
            new DurableDispatchIntakeHooks(boundary =>
            {
                if (boundary == DispatchIntakeBoundary.DispatchRecordCommitted)
                    throw new SimulatedCrashException();
            }));
        await Should.ThrowAsync<SimulatedCrashException>(async () =>
            await interrupted.ExecuteAsync(request));
        fixture.DeleteStoredPlans();

        var rejected = (await fixture.CreateFreshOperation("principal-001")
            .ExecuteAsync(request)).Unwrap();

        rejected.Disposition.ShouldBe(DurablePrepareDisposition.RejectedPlanUnavailable);
        rejected.State.ShouldBe(OperationLifecycleState.RejectedPlanUnavailable);
        fixture.SnapshotRepository().ShouldBe(before);
    }

    [Fact]
    public async Task corrupt_retained_dispatch_is_a_typed_corrupt_state_error()
    {
        using var fixture = new PrepareFixture();
        var request = fixture.CreateRequest();
        var before = fixture.SnapshotRepository();
        var interrupted = fixture.CreateFreshOperation(
            "principal-001",
            new DurableDispatchIntakeHooks(boundary =>
            {
                if (boundary == DispatchIntakeBoundary.DispatchRecordCommitted)
                    throw new SimulatedCrashException();
            }));
        await Should.ThrowAsync<SimulatedCrashException>(async () =>
            await interrupted.ExecuteAsync(request));
        fixture.CorruptDispatchRecord();

        var error = Error(await fixture.CreateFreshOperation("principal-001")
            .ExecuteAsync(request));

        error.Kind.ShouldBe(RtErrorKind.CorruptState);
        fixture.SnapshotRepository().ShouldBe(before);
    }

    [Fact]
    public async Task dispatch_disappearing_before_receive_is_typed_indeterminate()
    {
        using var fixture = new PrepareFixture();
        var before = fixture.SnapshotRepository();
        var operation = fixture.CreateFreshOperation(
            "principal-001",
            new DurableDispatchIntakeHooks(boundary =>
            {
                if (boundary == DispatchIntakeBoundary.DispatchReadyCommitted)
                    fixture.DeleteDispatchRecords();
            }));

        var error = Error(await operation.ExecuteAsync(fixture.CreateRequest()));

        error.Kind.ShouldBe(RtErrorKind.OperationIndeterminate);
        error.Message.ShouldNotBeNullOrWhiteSpace();
        fixture.SnapshotRepository().ShouldBe(before);
    }

    [Fact]
    public async Task a_fresh_application_composition_reconciles_the_retained_prepare_to_replay()
    {
        using var fixture = new PrepareFixture();
        var request = fixture.CreateRequest();
        var prepared = (await fixture.Operation.ExecuteAsync(request)).Unwrap();

        var restarted = fixture.CreateFreshOperation("principal-001");
        var replay = (await restarted.ExecuteAsync(request)).Unwrap();

        replay.Disposition.ShouldBe(DurablePrepareDisposition.Replay);
        replay.RequestHash.ShouldBe(prepared.RequestHash);
        fixture.LoadOperation(replay).Transitions.Select(transition => transition.State).ShouldBe([
            OperationLifecycleState.Received,
            OperationLifecycleState.Prepared
        ]);
    }

    [Theory]
    [MemberData(nameof(PersistenceBoundaries))]
    public async Task fresh_application_reconciles_after_every_persisted_prepare_boundary(
        int boundaryValue)
    {
        var boundary = (DispatchIntakeBoundary)boundaryValue;
        using var fixture = new PrepareFixture();
        var request = fixture.CreateRequest();
        var before = fixture.SnapshotRepository();
        var interrupted = fixture.CreateFreshOperation(
            "principal-001",
            new DurableDispatchIntakeHooks(reached =>
            {
                if (reached == boundary)
                    throw new SimulatedCrashException();
            }));

        await Should.ThrowAsync<SimulatedCrashException>(async () =>
            await interrupted.ExecuteAsync(request));

        var resumed = (await fixture.CreateFreshOperation("principal-001")
            .ExecuteAsync(request)).Unwrap();
        resumed.Disposition.ShouldBeOneOf(
            DurablePrepareDisposition.Prepared,
            DurablePrepareDisposition.Replay);
        resumed.State.ShouldBe(OperationLifecycleState.Prepared);
        fixture.SnapshotRepository().ShouldBe(before);
    }

    [Fact]
    public void local_authority_owns_fixed_external_roots_and_a_stable_os_principal()
    {
        using var fixture = new PrepareFixture();
        var controlRoot = Path.Combine(Path.GetDirectoryName(fixture.RepositoryRoot)!, "protected-authority");
        var authority = new LocalDurableOperationAuthority(controlRoot);

        var planError = ((IChangePlanStoreProtectionAuthority)authority).AcquireForPersistence(
            authority.PlanStoreRoot,
            fixture.Envelope.Payload.Repository,
            out var planLease);
        var ledgerError = ((IOperationLedgerProtectionAuthority)authority).Acquire(
            authority.LedgerRoot,
            out var ledgerLease);
        using (planLease)
        using (ledgerLease)
        {
            planError.ShouldBeNull();
            ledgerError.ShouldBeNull();
            planLease.ShouldNotBeNull();
            ledgerLease.ShouldNotBeNull();
            Path.GetFullPath(authority.PlanStoreRoot).ShouldStartWith(Path.GetFullPath(controlRoot));
            Path.GetFullPath(authority.LedgerRoot).ShouldStartWith(Path.GetFullPath(controlRoot));
            authority.GetPrincipal().ShouldBe(new LocalDurableOperationAuthority(controlRoot).GetPrincipal());
            if (!OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(authority.PlanStoreRoot).ShouldBe(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.GetUnixFileMode(authority.LedgerRoot).ShouldBe(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            new FileInfo(Path.Combine(authority.PlanStoreRoot, ".physical-protection.lease"))
                .Length.ShouldBe(1);
            new FileInfo(Path.Combine(authority.LedgerRoot, ".physical-protection.lease"))
                .Length.ShouldBe(1);
        }
    }

    [Fact]
    public void local_authority_reports_an_existing_ledger_root_without_a_lease_as_missing()
    {
        using var fixture = new PrepareFixture();
        var controlRoot = Path.Combine(Path.GetDirectoryName(fixture.RepositoryRoot)!, "incomplete-authority");
        var authority = new LocalDurableOperationAuthority(controlRoot);
        Directory.CreateDirectory(authority.LedgerRoot);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                authority.LedgerRoot,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var error = ((IOperationLedgerProtectionAuthority)authority).AcquireExisting(
            authority.LedgerRoot,
            out var lease);

        lease.ShouldBeNull();
        error.ShouldNotBeNull();
        error.Kind.ShouldBe(OperationLedgerErrorKind.Missing);
        File.Exists(Path.Combine(authority.LedgerRoot, ".physical-protection.lease")).ShouldBeFalse();
    }

    [Fact]
    public void local_authority_rejects_a_control_root_inside_the_repository()
    {
        using var fixture = new PrepareFixture();
        var authority = new LocalDurableOperationAuthority(
            Path.Combine(fixture.RepositoryRoot, ".unsafe-control"));

        var error = ((IChangePlanStoreProtectionAuthority)authority).AcquireForPersistence(
            authority.PlanStoreRoot,
            fixture.Envelope.Payload.Repository,
            out var lease);

        lease.ShouldBeNull();
        error.ShouldNotBeNull();
        error.Kind.ShouldBe(ChangePlanStoreErrorKind.UnsafeProtection);
        Directory.Exists(authority.PlanStoreRoot).ShouldBeFalse();
    }

    [Fact]
    public void local_authority_rejects_permission_drift_without_repairing_existing_root()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new PrepareFixture();
        var controlRoot = Path.Combine(Path.GetDirectoryName(fixture.RepositoryRoot)!, "drifted-authority");
        var authority = new LocalDurableOperationAuthority(controlRoot);
        Directory.CreateDirectory(authority.PlanStoreRoot);
        var driftedMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                          | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute;
        File.SetUnixFileMode(authority.PlanStoreRoot, driftedMode);

        var error = ((IChangePlanStoreProtectionAuthority)authority).AcquireForPersistence(
            authority.PlanStoreRoot,
            fixture.Envelope.Payload.Repository,
            out var lease);

        lease.ShouldBeNull();
        error.ShouldNotBeNull();
        error.Kind.ShouldBe(ChangePlanStoreErrorKind.UnsafeProtection);
        File.GetUnixFileMode(authority.PlanStoreRoot).ShouldBe(driftedMode);
    }

    [Fact]
    public void local_authority_authenticates_bounded_epoch_claims_and_rejects_tampering()
    {
        using var fixture = new PrepareFixture();
        var controlRoot = Path.Combine(Path.GetDirectoryName(fixture.RepositoryRoot)!, "epoch-authority");
        var authority = new LocalDurableOperationAuthority(controlRoot, () => InitialNow);
        var epoch = LocalDurableOperationAuthority.CurrentEpoch(InitialNow);
        var operationId = new OperationId(
            epoch,
            Guid.Parse("99999999-8888-7777-6666-555555555555"));

        var authenticated = authority.Authenticate(
            "repository-001",
            operationId,
            maximumOperationCount: 8,
            maximumRequestBytes: 16 * 1024 * 1024,
            LocalDurableOperationAuthority.EpochNotAfter(epoch),
            actualRequestBytes: 1024);
        var tampered = authority.Authenticate(
            "repository-001",
            operationId,
            maximumOperationCount: 9,
            maximumRequestBytes: 16 * 1024 * 1024,
            LocalDurableOperationAuthority.EpochNotAfter(epoch),
            actualRequestBytes: 1024);

        authenticated.IsOk.ShouldBeTrue();
        authenticated.Match(value => value.Principal, _ => null!).ShouldBe(authority.GetPrincipal());
        Error(tampered.Map(_ => default(DurablePrepareResult)!)).Kind
            .ShouldBe(RtErrorKind.OperationAccessDenied);
    }

    [Fact]
    public async Task production_authority_denial_does_not_provision_a_ledger_root_or_lease()
    {
        var now = DateTimeOffset.UtcNow.ToUniversalTime();
        using var fixture = new PrepareFixture(now);
        var controlRoot = Path.Combine(Path.GetDirectoryName(fixture.RepositoryRoot)!, "denied-control");
        var authority = new LocalDurableOperationAuthority(controlRoot, () => now);
        var planStore = new DurableChangePlanStore(
            authority.PlanStoreRoot,
            utcNow: () => now,
            protectionAuthority: authority);
        var ledger = new DurableOperationLedger(
            authority.LedgerRoot,
            utcNow: () => now,
            protectionAuthority: authority);
        var operation = new DurablePrepareOperation(
            planStore,
            new DurableDispatchIntakeCoordinator(planStore, ledger),
            authority);
        var epoch = LocalDurableOperationAuthority.CurrentEpoch(now);
        var tamperedRequest = fixture.CreateRequest(
            operationEpoch: epoch,
            maximumRequestBytes: 16 * 1024 * 1024,
            operationEpochNotAfterUtc: LocalDurableOperationAuthority.EpochNotAfter(epoch)) with
        {
            MaximumOperationCount = 9
        };

        var error = Error(await operation.ExecuteAsync(tamperedRequest));

        error.Kind.ShouldBe(RtErrorKind.OperationAccessDenied);
        Directory.Exists(authority.LedgerRoot).ShouldBeFalse();
        File.Exists(Path.Combine(authority.LedgerRoot, ".physical-protection.lease")).ShouldBeFalse();
    }

    [Fact]
    public async Task production_authority_executes_the_no_effect_prepare_against_external_roots()
    {
        var now = DateTimeOffset.UtcNow.ToUniversalTime();
        using var fixture = new PrepareFixture(now);
        var before = fixture.SnapshotRepository();
        var controlRoot = Path.Combine(Path.GetDirectoryName(fixture.RepositoryRoot)!, "production-control");
        var authority = new LocalDurableOperationAuthority(controlRoot, () => now);
        var planStore = new DurableChangePlanStore(
            authority.PlanStoreRoot,
            utcNow: () => now,
            protectionAuthority: authority);
        var ledger = new DurableOperationLedger(
            authority.LedgerRoot,
            utcNow: () => now,
            protectionAuthority: authority);
        var operation = new DurablePrepareOperation(
            planStore,
            new DurableDispatchIntakeCoordinator(planStore, ledger),
            authority);
        planStore.Persist(fixture.Envelope).IsOk.ShouldBeTrue();
        var epoch = LocalDurableOperationAuthority.CurrentEpoch(now);

        var prepared = (await operation.ExecuteAsync(fixture.CreateRequest(
            operationEpoch: epoch,
            maximumRequestBytes: 16 * 1024 * 1024,
            operationEpochNotAfterUtc: LocalDurableOperationAuthority.EpochNotAfter(epoch)))).Unwrap();

        prepared.Disposition.ShouldBe(DurablePrepareDisposition.Prepared);
        fixture.SnapshotRepository().ShouldBe(before);
        Directory.Exists(authority.PlanStoreRoot).ShouldBeTrue();
        Directory.Exists(authority.LedgerRoot).ShouldBeTrue();
    }

    [Fact]
    public async Task retained_exact_replay_survives_the_local_admission_epoch_boundary()
    {
        var now = DateTimeOffset.UtcNow.ToUniversalTime();
        var authorityNow = now;
        using var fixture = new PrepareFixture(now);
        var controlRoot = Path.Combine(Path.GetDirectoryName(fixture.RepositoryRoot)!, "replay-control");
        var authority = new LocalDurableOperationAuthority(controlRoot, () => authorityNow);
        var planStore = new DurableChangePlanStore(
            authority.PlanStoreRoot,
            utcNow: () => authorityNow,
            protectionAuthority: authority);
        var ledger = new DurableOperationLedger(
            authority.LedgerRoot,
            utcNow: () => authorityNow,
            protectionAuthority: authority);
        var operation = new DurablePrepareOperation(
            planStore,
            new DurableDispatchIntakeCoordinator(planStore, ledger),
            authority);
        planStore.Persist(fixture.Envelope).IsOk.ShouldBeTrue();
        var epoch = LocalDurableOperationAuthority.CurrentEpoch(now);
        var request = fixture.CreateRequest(
            operationEpoch: epoch,
            maximumRequestBytes: 16 * 1024 * 1024,
            operationEpochNotAfterUtc: LocalDurableOperationAuthority.EpochNotAfter(epoch));
        (await operation.ExecuteAsync(request)).Unwrap().Disposition
            .ShouldBe(DurablePrepareDisposition.Prepared);

        authorityNow = authorityNow.AddHours(2);
        var replay = (await operation.ExecuteAsync(request)).Unwrap();

        replay.Disposition.ShouldBe(DurablePrepareDisposition.Replay);
        replay.State.ShouldBe(OperationLifecycleState.Prepared);
    }

    [Fact]
    public void production_dependency_injection_shares_one_protected_prepare_composition()
    {
        var services = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(services);
        using var provider = services.BuildServiceProvider();

        var authority = provider.GetRequiredService<LocalDurableOperationAuthority>();
        provider.GetRequiredService<IAuthenticatedPrincipalProvider>().ShouldBeSameAs(authority);
        provider.GetRequiredService<IAuthenticatedOperationContextProvider>().ShouldBeSameAs(authority);
        provider.GetRequiredService<IChangePlanStoreProtectionAuthority>().ShouldBeSameAs(authority);
        provider.GetRequiredService<IOperationLedgerProtectionAuthority>().ShouldBeSameAs(authority);
        provider.GetRequiredService<DurableChangePlanStore>().ShouldBeSameAs(
            provider.GetRequiredService<DurableChangePlanStore>());
        provider.GetRequiredService<DurableOperationLedger>().ShouldBeSameAs(
            provider.GetRequiredService<DurableOperationLedger>());
        provider.GetRequiredService<DurableDispatchIntakeCoordinator>().ShouldBeSameAs(
            provider.GetRequiredService<DurableDispatchIntakeCoordinator>());
        provider.GetRequiredService<DurablePrepareOperation>().ShouldNotBeNull();
    }

    [Fact]
    public void durable_prepare_is_in_the_public_catalog_as_a_root_lifecycle_meta_command()
    {
        var info = DurablePrepareCommandDefinition.CreateInfo();
        var definition = DurablePrepareCommandDefinition.CreateDefinition(info);

        info.Name.ShouldBe("prepare-operation");
        info.Mutates.ShouldBeFalse();
        info.JsonOutput.ShouldBeTrue();
        info.Category.ShouldBe("lifecycle");
        info.Group.ShouldBeNull();
        info.Placement.ShouldBe(CommandPlacement.RootOnly);
        info.WhenToUse.ShouldContain("durable no-effect Prepared lifecycle boundary");
        info.Examples.ShouldBe([
            "rt prepare-operation --repository-id repository-001 --plan-id 11111111-2222-3333-4444-555555555555 --plan-hash aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa --expected-revision 0123456789abcdef0123456789abcdef01234567 --operation-epoch 42 --operation-nonce 99999999-8888-7777-6666-555555555555 --maximum-operation-count 8 --maximum-request-bytes 16777216 --operation-epoch-not-after 2030-01-02T04:00:00Z --dispatch-lease-id dispatch-1 --dispatch-lease-expires-at 2030-01-02T03:14:05Z --intake-attempt-id attempt-1 --intake-attempt-expires-at 2030-01-02T03:09:05Z --json"
        ]);
        info.KnownIssues.ShouldBe([
            "A plan-producing command, full operation-epoch issuance, status lookup, and repository effects remain separate work."
        ]);
        info.SupportedFormats.ShouldBe([OutputFormat.Json]);
        definition.Info.ShouldBeSameAs(info);
        definition.RequestType.ShouldBe(typeof(DurablePrepareRequest));
        definition.ResponseType.ShouldBe(typeof(DurablePrepareResult));
        definition.McpVisible.ShouldBeTrue();
        definition.Safety.ShouldBeSameAs(CommandSafety.LocalStateWrite);
        definition.ExecutionPlacement.ShouldBe(CommandExecutionPlacement.InProcess);
        definition.Product.Effects.ShouldBe(
            CommandEffects.LocalStateRead | CommandEffects.LocalStateWrite);
        definition.Product.ConditionallyMutable.ShouldBeTrue();
        definition.Product.Prerequisites.ShouldBe([
            "Exact rt.change-plan.v1 envelope already retained in the protected plan store"
        ]);
        definition.Product.Limits.MaximumOutputBytes.ShouldBe(64 * 1024);
        definition.Product.Limitations.ShouldBe(info.KnownIssues);
        definition.Product.Examples.ShouldBe(info.Examples);
        DurablePrepareCommandDefinition.CreateDefinition(info with { KnownIssues = null })
            .Product.Limitations.ShouldBeEmpty();
        App.CatalogCommands.ShouldContain(entry => entry.Info.Name == "prepare-operation");
        App.Commands.ShouldNotContain(entry => entry.Info.Name == "prepare-operation");
        new AppCommandCatalog().All.ShouldContain(info => info.Name == "prepare-operation");
    }

    [Fact]
    public async Task cli_reaches_the_same_typed_prepared_result_without_repository_effects()
    {
        var now = DateTimeOffset.UtcNow.ToUniversalTime();
        using var fixture = new HostFixture(now);
        var before = SnapshotRepository(fixture.RepositoryRoot);
        var services = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(services);
        RegisterProductionComposition(services, fixture.ProtectedControlRoot, now);
        using var provider = services.BuildServiceProvider();
        fixture.SeedPlan(provider);
        using var console = new CapturingConsole();
        using var scope = RoslynTools.Core.RtConsole.UseConsole(console);
        var epoch = LocalDurableOperationAuthority.CurrentEpoch(now);

        var exitCode = await App.RunAsync([
            "prepare-operation",
            "--repository-id", "repository-001",
            "--plan-id", fixture.PlanId.ToString(),
            "--plan-hash", fixture.PlanHash,
            "--expected-revision", fixture.ExpectedRevision,
            "--operation-epoch", epoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--operation-nonce", "99999999-8888-7777-6666-555555555555",
            "--maximum-operation-count", "8",
            "--maximum-request-bytes", "16777216",
            "--operation-epoch-not-after",
            LocalDurableOperationAuthority.EpochNotAfter(epoch).ToString("O"),
            "--dispatch-lease-id", "dispatch-1",
            "--dispatch-lease-expires-at", now.AddMinutes(10).ToString("O"),
            "--intake-attempt-id", "attempt-1",
            "--intake-attempt-expires-at", now.AddMinutes(5).ToString("O"),
            "--json"
        ], provider);

        exitCode.ShouldBe(0);
        using var payload = System.Text.Json.JsonDocument.Parse(console.Output);
        payload.RootElement.GetProperty("disposition").GetString().ShouldBe("Prepared");
        payload.RootElement.GetProperty("state").GetString().ShouldBe("Prepared");
        SnapshotRepository(fixture.RepositoryRoot).ShouldBe(before);
        console.Error.ShouldBeEmpty();
    }

    [Fact]
    public async Task cli_preserves_the_shared_typed_prepare_error()
    {
        var services = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(services);
        using var provider = services.BuildServiceProvider();
        using var console = new CapturingConsole();
        using var scope = RoslynTools.Core.RtConsole.UseConsole(console);

        var exitCode = await App.RunAsync([
            "prepare-operation",
            "--repository-id", "repository-001",
            "--plan-id", "11111111-2222-3333-4444-555555555555",
            "--plan-hash", "not-a-sha256",
            "--expected-revision", new string('a', 40),
            "--operation-epoch", "42",
            "--operation-nonce", "99999999-8888-7777-6666-555555555555",
            "--maximum-operation-count", "8",
            "--maximum-request-bytes", "1048576",
            "--operation-epoch-not-after", "2030-01-02T04:04:05+00:00",
            "--dispatch-lease-id", "dispatch-1",
            "--dispatch-lease-expires-at", "2030-01-02T03:14:05+00:00",
            "--intake-attempt-id", "attempt-1",
            "--intake-attempt-expires-at", "2030-01-02T03:09:05+00:00",
            "--json"
        ], provider);

        exitCode.ShouldBe(1);
        using var payload = System.Text.Json.JsonDocument.Parse(console.Output);
        payload.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.InvalidInput));
        payload.RootElement.GetProperty("message").GetString()!.ShouldContain("SHA-256");
        console.Error.ShouldBeEmpty();
    }

    private sealed class PrepareFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"rt-durable-prepare-tests-{Guid.NewGuid():N}");
        private readonly DurableOperationLedger _ledger;
        private readonly DurableChangePlanStore _planStore;
        private readonly DurableDispatchIntakeCoordinator _coordinator;
        private readonly TestProtectionAuthority _authority;
        private DateTimeOffset _now;

        internal PrepareFixture(DateTimeOffset? now = null)
        {
            _now = (now ?? InitialNow).ToUniversalTime();
            RepositoryRoot = Path.Combine(_root, "repository");
            PlanStoreRoot = Path.Combine(_root, "control", "plans");
            LedgerRoot = Path.Combine(_root, "control", "ledger");
            ChangeRoot = Path.Combine(_root, "change-control");
            Directory.CreateDirectory(Path.Combine(RepositoryRoot, "src"));
            Directory.CreateDirectory(ChangeRoot);
            File.WriteAllText(Path.Combine(RepositoryRoot, "src", "Existing.cs"), "class Existing;\n");

            Envelope = CreateEnvelope();
            _authority = new TestProtectionAuthority(PlanStoreRoot, LedgerRoot);
            _planStore = new DurableChangePlanStore(
                PlanStoreRoot,
                utcNow: () => _now,
                protectionAuthority: _authority);
            _ledger = new DurableOperationLedger(
                LedgerRoot,
                utcNow: () => _now,
                protectionAuthority: _authority);
            _coordinator = new DurableDispatchIntakeCoordinator(_planStore, _ledger);
            var persisted = _planStore.Persist(Envelope);
            persisted.IsOk.ShouldBeTrue();
            Operation = CreateOperation("principal-001");
        }

        internal string RepositoryRoot { get; }
        internal string PlanStoreRoot { get; }
        internal string LedgerRoot { get; }
        internal string ChangeRoot { get; }
        internal ChangePlanEnvelope Envelope { get; }
        internal DurablePrepareOperation Operation { get; }
        internal DurableChangePlanStore PlanStore => _planStore;
        internal DurableDispatchIntakeCoordinator Coordinator => _coordinator;

        internal DurablePrepareOperation CreateOperation(string principal) => new(
            _planStore,
            _coordinator,
            new TestPrincipalProvider(principal));

        internal DurablePrepareOperation CreateOperation(
            IAuthenticatedOperationContextProvider contextProvider) => new(
            _planStore,
            _coordinator,
            contextProvider);

        internal DurablePrepareOperation CreateFreshOperation(
            string principal,
            DurableDispatchIntakeHooks? hooks = null)
        {
            var planStore = new DurableChangePlanStore(
                PlanStoreRoot,
                utcNow: () => _now,
                protectionAuthority: _authority);
            var ledger = new DurableOperationLedger(
                LedgerRoot,
                utcNow: () => _now,
                protectionAuthority: _authority);
            return new DurablePrepareOperation(
                planStore,
                new DurableDispatchIntakeCoordinator(planStore, ledger, hooks),
                new TestPrincipalProvider(principal));
        }

        internal DurablePrepareRequest CreateRequest(
            ChangePlanEnvelope? envelope = null,
            long operationEpoch = 42,
            long maximumRequestBytes = 1024 * 1024,
            DateTimeOffset? operationEpochNotAfterUtc = null,
            string? expectedRevision = null,
            DateTimeOffset? dispatchLeaseExpiresAtUtc = null) => new(
            (envelope ?? Envelope).Payload.Repository.RepositoryId,
            (envelope ?? Envelope).Payload.PlanId,
            (envelope ?? Envelope).PlanHash,
            expectedRevision ?? (envelope ?? Envelope).Payload.Repository.BaseRevision,
            OperationEpoch: operationEpoch,
            OperationNonce: Guid.Parse("99999999-8888-7777-6666-555555555555"),
            MaximumOperationCount: 8,
            MaximumRequestBytes: maximumRequestBytes,
            OperationEpochNotAfterUtc: operationEpochNotAfterUtc ?? _now + TimeSpan.FromHours(1),
            DispatchLeaseId: "dispatch-1",
            DispatchLeaseExpiresAtUtc: dispatchLeaseExpiresAtUtc ?? _now + TimeSpan.FromMinutes(10),
            IntakeAttemptId: "attempt-1",
            IntakeAttemptExpiresAtUtc: _now + TimeSpan.FromMinutes(5));

        internal DurableOperationRecord LoadOperation(DurablePrepareResult result) =>
            Unwrap(_ledger.Load(
                result.RepositoryId,
                new OperationId(result.OperationEpoch, result.OperationNonce)));

        internal string SnapshotRepository()
        {
            var entries = Directory.EnumerateFileSystemEntries(
                    RepositoryRoot,
                    "*",
                    SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(path => Directory.Exists(path)
                    ? $"d:{Path.GetRelativePath(RepositoryRoot, path)}"
                    : $"f:{Path.GetRelativePath(RepositoryRoot, path)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}")
                .ToArray();
            return string.Join('\n', entries);
        }

        internal string SnapshotPlanStore() => SnapshotDirectory(PlanStoreRoot);

        internal void Persist(ChangePlanEnvelope envelope)
        {
            _planStore.Persist(envelope).IsOk.ShouldBeTrue();
        }

        internal void AdvanceTo(DateTimeOffset now) => _now = now.ToUniversalTime();

        internal void DeleteStoredPlans()
        {
            foreach (var path in Directory.EnumerateFiles(PlanStoreRoot, "*.plan", SearchOption.AllDirectories))
                File.Delete(path);
        }

        internal void CorruptDispatchRecord()
        {
            var path = Directory.EnumerateFiles(LedgerRoot, "*.dispatch", SearchOption.AllDirectories)
                .Single();
            File.WriteAllText(path, "{}");
        }

        internal void DeleteDispatchRecords()
        {
            foreach (var path in Directory.EnumerateFiles(LedgerRoot, "*.dispatch", SearchOption.AllDirectories))
                File.Delete(path);
        }

        internal ChangePlanEnvelope CreateEnvelope(
            Guid? planId = null,
            DateTimeOffset? expiresAtUtc = null)
        {
            const string sourcePath = "src/Added.cs";
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
            var expiresAt = expiresAtUtc?.ToUniversalTime() ?? _now + TimeSpan.FromMinutes(20);
            var payload = new ChangePlanPayload(
                planId ?? Guid.Parse("11111111-2222-3333-4444-555555555555"),
                new ChangePlanRepository(
                    "repository-001",
                    RepositoryRoot,
                    RepositoryRoot,
                    "device:test;inode:repository",
                    new string('a', 40),
                    new string('b', 64)),
                expiresAtUtc.HasValue ? expiresAt - TimeSpan.FromMinutes(1) : _now - TimeSpan.FromMinutes(1),
                expiresAt,
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
                    ChangeRoot,
                    ChangeRoot,
                    "device:test;inode:control",
                    "device:test",
                    Path.Combine(ChangeRoot, "staging"),
                    Path.Combine(ChangeRoot, "staging"),
                    "device:test;inode:staging",
                    Path.Combine(ChangeRoot, "backup"),
                    Path.Combine(ChangeRoot, "backup"),
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

        private static string SnapshotDirectory(string root)
        {
            var entries = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(path => Directory.Exists(path)
                    ? $"d:{Path.GetRelativePath(root, path)}"
                    : $"f:{Path.GetRelativePath(root, path)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}")
                .ToArray();
            return string.Join('\n', entries);
        }

        private static T Unwrap<T>(Result<T, OperationLedgerError> result) =>
            result.Match(
                value => value,
                error => throw new ShouldAssertException($"Expected success, got {error.Kind}: {error.Message}"));
    }

    private sealed class TestPrincipalProvider(string value)
        : IAuthenticatedPrincipalProvider, IAuthenticatedOperationContextProvider
    {
        public AuthenticatedPrincipalId GetPrincipal() => new(value);

        public Result<AuthenticatedOperationContext, RtError> Authenticate(
            string repositoryId,
            OperationId operationId,
            int maximumOperationCount,
            long maximumRequestBytes,
            DateTimeOffset notAfterUtc,
            long actualRequestBytes) => Result<AuthenticatedOperationContext, RtError>.Ok(new(
                GetPrincipal(),
                new OperationEpochClaims(
                    repositoryId,
                    value,
                    operationId.Epoch,
                    maximumOperationCount,
                    maximumRequestBytes,
                    notAfterUtc)));
    }

    private sealed class MismatchedContextProvider : IAuthenticatedOperationContextProvider
    {
        public AuthenticatedPrincipalId GetPrincipal() => new("principal-001");

        public Result<AuthenticatedOperationContext, RtError> Authenticate(
            string repositoryId,
            OperationId operationId,
            int maximumOperationCount,
            long maximumRequestBytes,
            DateTimeOffset notAfterUtc,
            long actualRequestBytes) => Result<AuthenticatedOperationContext, RtError>.Ok(new(
            new AuthenticatedPrincipalId("principal-002"),
            new OperationEpochClaims(
                repositoryId,
                "principal-002",
                operationId.Epoch,
                maximumOperationCount,
                maximumRequestBytes,
                notAfterUtc)));
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

    private sealed class ProtectionLease
        : IChangePlanStoreProtectionLease, IOperationLedgerProtectionLease
    {
        public void Dispose()
        {
        }
    }

    private sealed class SimulatedCrashException : Exception
    {
        internal SimulatedCrashException()
        {
        }

        internal SimulatedCrashException(string message)
            : base(message)
        {
        }

        internal SimulatedCrashException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    private sealed class CapturingConsole : IRtConsole, IDisposable
    {
        private readonly StringWriter _stdout = new();
        private readonly StringWriter _stderr = new();

        public bool IsOutputRedirected => true;

        public TextWriter Out => _stdout;

        internal string Output => _stdout.ToString();

        internal string Error => _stderr.ToString();

        public void Log(RtLogLevel level, string template, params object?[] args) =>
            _stderr.WriteLine(RoslynTools.Core.RtConsole.RenderTemplate(template, args));

        public void WriteLine(
            RtOutputContext context,
            string message = "",
            ConsoleColor? color = null) => Writer(context).WriteLine(message);

        public void Write(
            RtOutputContext context,
            string message,
            ConsoleColor? color = null) => Writer(context).Write(message);

        public void Dispose()
        {
            _stdout.Dispose();
            _stderr.Dispose();
        }

        private StringWriter Writer(RtOutputContext context) =>
            context.HasFlag(RtOutputContext.Diagnostic) ? _stderr : _stdout;
    }

    private static RtError Error(Result<DurablePrepareResult, RtError> result) =>
        result.Match(
            _ => throw new ShouldAssertException("Expected a typed Prepare error."),
            error => error);

    private static void RegisterProductionComposition(
        IServiceCollection services,
        string controlRoot,
        DateTimeOffset now)
    {
        var authority = new LocalDurableOperationAuthority(controlRoot, () => now);
        services.AddSingleton(authority);
        services.AddSingleton<IAuthenticatedPrincipalProvider>(_ => authority);
        services.AddSingleton<IAuthenticatedOperationContextProvider>(_ => authority);
        services.AddSingleton<IChangePlanStoreProtectionAuthority>(_ => authority);
        services.AddSingleton<IOperationLedgerProtectionAuthority>(_ => authority);
        services.AddSingleton(provider => new DurableChangePlanStore(
            authority.PlanStoreRoot,
            utcNow: () => now,
            protectionAuthority: provider.GetRequiredService<IChangePlanStoreProtectionAuthority>()));
        services.AddSingleton(provider => new DurableOperationLedger(
            authority.LedgerRoot,
            utcNow: () => now,
            protectionAuthority: provider.GetRequiredService<IOperationLedgerProtectionAuthority>()));
        services.AddSingleton(provider => new DurableDispatchIntakeCoordinator(
            provider.GetRequiredService<DurableChangePlanStore>(),
            provider.GetRequiredService<DurableOperationLedger>()));
        services.AddSingleton<DurablePrepareOperation>();
    }

    private static string SnapshotRepository(string repositoryRoot)
    {
        var entries = Directory.EnumerateFileSystemEntries(
                repositoryRoot,
                "*",
                SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => Directory.Exists(path)
                ? $"d:{Path.GetRelativePath(repositoryRoot, path)}"
                : $"f:{Path.GetRelativePath(repositoryRoot, path)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}")
            .ToArray();
        return string.Join('\n', entries);
    }
}
