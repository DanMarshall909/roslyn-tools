using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

internal sealed record DurablePrepareRequest(
    [property: JsonRequired]
    [property: CommandPropertyDescription("Repository identity carried by the protected stored plan")]
    string RepositoryId,
    [property: JsonRequired]
    [property: CommandPropertyDescription("Immutable stored-plan identifier")]
    Guid PlanId,
    [property: JsonRequired]
    [property: CommandPropertyDescription("SHA-256 hash of the exact canonical stored-plan envelope")]
    string PlanHash,
    [property: JsonRequired]
    [property: CommandPropertyDescription("Repository revision the stored plan was prepared against")]
    string ExpectedRevision,
    [property: JsonRequired]
    [property: CommandPropertyDescription("Caller-known operation epoch")]
    long OperationEpoch,
    [property: JsonRequired]
    [property: CommandPropertyDescription("Caller-generated operation nonce")]
    Guid OperationNonce,
    [property: JsonRequired]
    [property: CommandPropertyDescription("Authenticated maximum operation count for the epoch")]
    int MaximumOperationCount,
    [property: JsonRequired]
    [property: CommandPropertyDescription("Authenticated maximum canonical-request bytes for the epoch")]
    long MaximumRequestBytes,
    [property: JsonRequired]
    [property: CommandPropertyDescription("Authenticated operation-epoch expiry in UTC")]
    DateTimeOffset OperationEpochNotAfterUtc,
    [property: JsonRequired]
    [property: CommandPropertyDescription("Stable caller-known dispatch-plan lease identifier")]
    string DispatchLeaseId,
    [property: JsonRequired]
    [property: CommandPropertyDescription("Dispatch-plan lease expiry in UTC")]
    DateTimeOffset DispatchLeaseExpiresAtUtc,
    [property: JsonRequired]
    [property: CommandPropertyDescription("Stable transport intake-attempt identifier")]
    string IntakeAttemptId,
    [property: JsonRequired]
    [property: CommandPropertyDescription("Intake-attempt lease expiry in UTC")]
    DateTimeOffset IntakeAttemptExpiresAtUtc);

[JsonConverter(typeof(JsonStringEnumConverter<DurablePrepareDisposition>))]
internal enum DurablePrepareDisposition
{
    Prepared,
    Replay,
    RejectedPlanUnavailable
}

internal sealed record DurablePrepareResult(
    DurablePrepareDisposition Disposition,
    string RepositoryId,
    long OperationEpoch,
    Guid OperationNonce,
    Guid PlanId,
    string PlanHash,
    string RequestHash,
    OperationLifecycleState State);

/// <summary>
///     Loads one exact stored canonical plan and advances its caller-known operation identity
///     through the durable no-effect intake handoff. Reaching Prepared authorizes no
///     repository write.
/// </summary>
internal sealed class DurablePrepareOperation(
    DurableChangePlanStore planStore,
    DurableDispatchIntakeCoordinator coordinator,
    IAuthenticatedOperationContextProvider operationContextProvider)
    : CommandApplicationPort<DurablePrepareRequest, DurablePrepareResult>, IRtService
{
    private readonly DurableChangePlanStore _planStore =
        planStore ?? throw new ArgumentNullException(nameof(planStore));
    private readonly DurableDispatchIntakeCoordinator _coordinator =
        coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    private readonly IAuthenticatedOperationContextProvider _operationContextProvider =
        operationContextProvider ?? throw new ArgumentNullException(nameof(operationContextProvider));

    public override ApplicationCommandDefinition Definition => DurablePrepareCommandDefinition.Definition;

    public override Task<Result<DurablePrepareResult, RtError>> ExecuteAsync(
        DurablePrepareRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        CanonicalApplyRequest canonicalRequest;
        AuthenticatedPrincipalId principal;
        OperationEpochClaims claimedEpoch;
        try
        {
            principal = _operationContextProvider.GetPrincipal();
            var operationId = new OperationId(request.OperationEpoch, request.OperationNonce);
            claimedEpoch = new OperationEpochClaims(
                request.RepositoryId,
                principal.Value,
                request.OperationEpoch,
                request.MaximumOperationCount,
                request.MaximumRequestBytes,
                request.OperationEpochNotAfterUtc);
            canonicalRequest = CanonicalApplyRequest.Create(
                request.RepositoryId,
                operationId,
                request.PlanId,
                request.PlanHash,
                request.ExpectedRevision,
                claimedEpoch);
        }
        catch (ArgumentException exception)
        {
            return Task.FromResult(Error(RtErrorKind.InvalidInput, exception.Message));
        }

        var registration = new DispatchPlanRegistration(
            canonicalRequest,
            principal,
            request.DispatchLeaseId,
            request.DispatchLeaseExpiresAtUtc,
            request.IntakeAttemptId,
            request.IntakeAttemptExpiresAtUtc);
        var inspected = _coordinator.InspectRequestIdentity(canonicalRequest, principal);
        if (inspected.IsErr)
        {
            return Task.FromResult(Result<DurablePrepareResult, RtError>.Err(
                MapCoordinatorError(inspected.Match(_ => null!, error => error))));
        }
        var inspection = inspected.Match(value => value, _ => null!);
        if (inspection.Disposition != DurableDispatchIntakeDisposition.Ready)
            return Task.FromResult(MapOutcome(canonicalRequest, inspection));

        // A retained matching identity already authenticated and persisted these exact
        // canonical claims. Reconciliation must remain available after epoch admission
        // closes, so only a genuinely new identity is checked against current host policy.
        if (inspection.Dispatch is null)
        {
            var authenticated = _operationContextProvider.Authenticate(
                request.RepositoryId,
                canonicalRequest.OperationId,
                request.MaximumOperationCount,
                request.MaximumRequestBytes,
                request.OperationEpochNotAfterUtc,
                canonicalRequest.CanonicalBytes.Length);
            if (authenticated.IsErr)
                return Task.FromResult(authenticated.Map(_ => default(DurablePrepareResult)!));
            var context = authenticated.Match(value => value, _ => null!);
            if (context.Principal != principal
                || !string.Equals(
                    context.EpochClaims.ClaimsHash,
                    claimedEpoch.ClaimsHash,
                    StringComparison.Ordinal))
            {
                return Task.FromResult(Error(
                    RtErrorKind.OperationAccessDenied,
                    "The protected host authority did not authenticate the submitted operation claims."));
            }

            var stored = _planStore.Load(new ChangePlanStoreKey(
                request.RepositoryId,
                request.PlanId,
                request.PlanHash));
            if (stored.IsErr)
            {
                return Task.FromResult(Result<DurablePrepareResult, RtError>.Err(
                    MapPlanStoreError(stored.Match(_ => null!, error => error))));
            }
            var envelope = stored.Match(value => value.Envelope, _ => null!);
            if (!string.Equals(
                    envelope.Payload.Repository.BaseRevision,
                    request.ExpectedRevision,
                    StringComparison.Ordinal))
            {
                return Task.FromResult(Error(
                    RtErrorKind.PlanUnavailable,
                    "The stored plan does not match the requested repository revision."));
            }
        }

        Result<DurableDispatchIntakeResult, DurableDispatchIntakeError> registered;
        try
        {
            registered = _coordinator.RegisterDispatchPlan(registration);
        }
        catch (ArgumentException exception)
        {
            return Task.FromResult(Error(RtErrorKind.InvalidInput, exception.Message));
        }
        if (registered.IsErr)
        {
            return Task.FromResult(Result<DurablePrepareResult, RtError>.Err(
                MapCoordinatorError(registered.Match(_ => null!, error => error))));
        }

        var registrationOutcome = registered.Match(value => value, _ => null!);
        if (registrationOutcome.Disposition != DurableDispatchIntakeDisposition.Ready)
            return Task.FromResult(MapOutcome(canonicalRequest, registrationOutcome));

        var received = _coordinator.ReceiveAndPrepare(canonicalRequest, principal);
        return Task.FromResult(received.Match(
            outcome => MapOutcome(canonicalRequest, outcome),
            error => Result<DurablePrepareResult, RtError>.Err(MapCoordinatorError(error))));
    }

    private static Result<DurablePrepareResult, RtError> MapOutcome(
        CanonicalApplyRequest request,
        DurableDispatchIntakeResult outcome)
    {
        if (outcome.Disposition == DurableDispatchIntakeDisposition.Conflict)
        {
            return Error(
                RtErrorKind.OperationConflict,
                "The operation identity is already bound to different canonical work.");
        }
        if (outcome.Disposition == DurableDispatchIntakeDisposition.AccessDenied)
        {
            return Error(
                RtErrorKind.OperationAccessDenied,
                "The authenticated principal is not authorized for this operation.");
        }
        if (outcome.Disposition is DurableDispatchIntakeDisposition.Indeterminate
            or DurableDispatchIntakeDisposition.Ready)
        {
            return Error(
                RtErrorKind.OperationIndeterminate,
                "The durable Prepare outcome is indeterminate; retry the exact same operation identity.");
        }

        var state = outcome.Operation?.CurrentState;
        // Stryker disable once Block : Valid coordinator results always carry an operation for non-rejection terminal outcomes; this fail-closed branch documents that invariant.
        if (state is null
            && outcome.Disposition != DurableDispatchIntakeDisposition.RejectedPlanUnavailable)
        {
            return Error(
                RtErrorKind.CorruptState,
                // Stryker disable once String : This defensive coordinator-invariant diagnostic is unreachable through a valid coordinator result; the typed branch remains explicit.
                "The durable Prepare outcome did not include its operation record.");
        }

        var disposition = outcome.Disposition switch
        {
            DurableDispatchIntakeDisposition.Prepared => DurablePrepareDisposition.Prepared,
            DurableDispatchIntakeDisposition.Replay => DurablePrepareDisposition.Replay,
            DurableDispatchIntakeDisposition.RejectedPlanUnavailable =>
                DurablePrepareDisposition.RejectedPlanUnavailable,
            _ => throw new InvalidOperationException(
                // Stryker disable once String : The closed coordinator disposition mapping makes this fallback unreachable; wording is diagnostic.
                $"Unsupported durable Prepare disposition '{outcome.Disposition}'.")
        };
        return Result<DurablePrepareResult, RtError>.Ok(new DurablePrepareResult(
            disposition,
            request.RepositoryId,
            request.OperationId.Epoch,
            request.OperationId.Nonce,
            request.Identity.PlanId,
            request.Identity.PlanHash,
            request.Identity.RequestHash,
            state ?? OperationLifecycleState.RejectedPlanUnavailable));
    }

    private static RtError MapPlanStoreError(ChangePlanStoreError error) => new(
        error.Kind switch
        {
            ChangePlanStoreErrorKind.Expired or ChangePlanStoreErrorKind.Missing
                or ChangePlanStoreErrorKind.SchemaIncompatible
                or ChangePlanStoreErrorKind.HashMismatch => RtErrorKind.PlanUnavailable,
            ChangePlanStoreErrorKind.UnsafeProtection => RtErrorKind.UnsafeCoordination,
            ChangePlanStoreErrorKind.Conflict => RtErrorKind.OperationConflict,
            ChangePlanStoreErrorKind.Unreadable => RtErrorKind.CorruptState,
            ChangePlanStoreErrorKind.CapacityExceeded => RtErrorKind.CoordinationUnavailable,
            _ => RtErrorKind.CoordinationUnavailable
        },
        error.Message,
        error.Evidence);

    private static RtError MapCoordinatorError(DurableDispatchIntakeError error) => new(
        error.Kind switch
        {
            DurableDispatchIntakeErrorKind.UnsafeProtection => RtErrorKind.UnsafeCoordination,
            DurableDispatchIntakeErrorKind.CorruptState => RtErrorKind.CorruptState,
            DurableDispatchIntakeErrorKind.CoordinationUnavailable => RtErrorKind.CoordinationUnavailable,
            _ => RtErrorKind.CoordinationUnavailable
        },
        error.Message,
        error.Evidence);

    private static Result<DurablePrepareResult, RtError> Error(
        RtErrorKind kind,
        string message) => Result<DurablePrepareResult, RtError>.Err(new RtError(kind, message));
}

internal static class DurablePrepareCommandDefinition
{
    internal static RtCommandInfo Info { get; } = CreateInfo();

    internal static ApplicationCommandDefinition Definition { get; } = CreateDefinition(Info);

    internal static RtCommandInfo CreateInfo() => new(
        "prepare-operation",
        false,
        true,
        "lifecycle",
        "Use this when an exact canonical plan is already in the protected store and you need the durable no-effect Prepared lifecycle boundary. This writes protected host-local coordination state, never writes repository files, and does not apply the plan.",
        ["rt maintainer prepare-operation --repository-id repository-001 --plan-id 11111111-2222-3333-4444-555555555555 --plan-hash aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa --expected-revision 0123456789abcdef0123456789abcdef01234567 --operation-epoch 42 --operation-nonce 99999999-8888-7777-6666-555555555555 --maximum-operation-count 8 --maximum-request-bytes 16777216 --operation-epoch-not-after 2030-01-02T04:00:00Z --dispatch-lease-id dispatch-1 --dispatch-lease-expires-at 2030-01-02T03:14:05Z --intake-attempt-id attempt-1 --intake-attempt-expires-at 2030-01-02T03:09:05Z --json"],
        KnownIssues:
        [
            "A plan-producing command, full operation-epoch issuance, status lookup, and repository effects remain separate work."
        ],
        SupportedFormats: [OutputFormat.Json],
        Placement: CommandPlacement.RootOnly);

    internal static ApplicationCommandDefinition CreateDefinition(RtCommandInfo info) => new(
        info,
        typeof(DurablePrepareRequest),
        typeof(DurablePrepareResult),
        mcpVisible: false,
        CommandSafety.LocalStateWrite,
        CommandExecutionPlacement.InProcess,
        [],
        product: new CommandProductMetadata(
            CapabilityMaturity.Experimental,
            CommandEffects.LocalStateRead | CommandEffects.LocalStateWrite,
            ConditionallyMutable: true,
            CommandIdempotency.Conditional,
            CommandAudience.Maintainer,
            TrustRequirement.None,
            WorkspaceRequirement.None,
            Prerequisites: ["Exact rt.change-plan.v1 envelope already retained in the protected plan store"],
            CommandCost.Low,
            CancellationSupport.Cooperative,
            new CommandResourceLimits(
                DefaultTimeoutMilliseconds: 30_000,
                MaximumItems: 1,
                MaximumOutputBytes: 64 * 1024),
            info.KnownIssues ?? [],
            info.Examples));
}
