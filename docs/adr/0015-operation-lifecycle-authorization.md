# ADR-0015: Operation-lifecycle authorization

- Status: Accepted
- Date: 2026-08-31
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)
- Related ADRs: [ADR-0008](0008-daemon-transparent-passthrough-phase.md), [ADR-0009](0009-authenticated-daemon-endpoints.md)

## Context

`(RepositoryId, OperationId)` is the stable address for mutation intake, status,
cancellation, recovery, acknowledgement, cleanup, replay, and retention. It is not a
secret. In a shared authority, another principal can learn an operation ID from logs,
coordination, or a collaborator. Treating the composite address as authorization would
let that principal inspect protected evidence, cancel work, perform recovery effects,
or acknowledge and lift a recovery barrier.

Mutual authentication at a daemon endpoint identifies peers but does not decide which
repository lifecycle action the authenticated caller may perform. The same policy must
hold for CLI, MCP, direct in-process, daemon, and internal reconciliation paths.

## Decision

Every operation-lifecycle request carries an `AuthenticatedPrincipalId` derived from a
protected host/transport context and one declared `LifecycleAction`. Roles, principal
identity, and self-asserted authorization data are never accepted from repository content or an
ordinary request field. The coordination authority either evaluates current policy
directly or validates a short-lived, audience- and action-scoped
`LifecycleAuthorization` bound to that authenticated principal, repository, operation
scope, `CoordinationGeneration`, authorization generation, and expiry. The proof is
not bearer authority for another principal.

The authority reauthorizes before returning existence-specific data, acquiring an
intake/reservation/effect fence, issuing replay permission, releasing artifacts, or
changing ledger/barrier state. Transfer, role revocation, and authorization-generation
rotation invalidate stale proofs. The action matrix is:

- `ResolveDispatch` and `Replay` require the matching submitting principal and original
  request/token proof with current mutation permission, or the narrowly scoped
  authority reconciler.
- `ReadStatus` requires a currently permitted submitter with `ReadOwnOperation`, an
  `OperationReader`, `RecoveryOperator`, or `RepositoryLifecycleAdministrator`.
- `Cancel` requires a matching submitter with `CancelOwnOperation`, an
  `OperationCanceller`, or `RepositoryLifecycleAdministrator`.
- `Resume` and `Rollback` require `RecoveryOperator` or
  `RepositoryLifecycleAdministrator`.
- `AcknowledgeRecovery` requires the separate `RecoveryAcknowledger` role or
  `RepositoryLifecycleAdministrator`, deliberate invocation, and a reason; generic
  mutation or recovery permission is insufficient.
- `Cleanup` requires `RecoveryOperator`, `RepositoryLifecycleAdministrator`, or a
  narrowly scoped authority cleanup identity.
- `Reconcile` is available only to the authenticated coordination-authority service
  identity and only for deterministic fenced reconciliation.

An authorized status request may schedule deterministic reconciliation or cleanup, but
the caller does not inherit the service role. The authority derives the internal
operation inputs, runs under the constrained service identity, and reauthorizes the
result for `ReadStatus` before disclosure. An unauthorized lookup cannot trigger
service work as a confused deputy.

An unauthorized request returns `OperationAccessDenied` before any operation-specific
state, hashes, paths, actor identities, or existence distinction is disclosed and does
not mutate the operation ledger. A principal otherwise authorized to submit mutation
may receive only a generic `RepositoryRecoveryBlocked` barrier summary needed to block
new work; it does not receive the protected operation ID or evidence. Denial security
telemetry is rate-limited and compacted outside the operation ledger so hostile probes
cannot create another unbounded record.

Successful lifecycle transitions record the authenticated actor, effective role,
authorization generation, and action in bounded audit evidence. Operation submission
stores the submitting principal for later own-operation policy checks, but that stored
identity is not itself a permanent grant; current policy is evaluated on every call.

## Consequences

- Knowing an operation ID is never enough to read or mutate its lifecycle.
- Recovery acknowledgement has a stronger explicit role than ordinary mutation or
  recovery execution because it lifts a barrier without proving repository cleanliness.
- Ownership transfer and role revocation can remove access immediately through
  generation rotation; callers must reacquire action-scoped authorization.
- Internal startup/status reconciliation uses a constrained service identity rather
  than an unauthenticated bypass.
- Cross-adapter and cross-principal tests must prove denial causes no disclosure,
  cancellation, effect, cleanup, replay, artifact release, or barrier change.

## Alternatives considered

- Treat the operation ID as a capability: rejected because IDs appear in diagnostics
  and coordination workflows and cannot safely be kept secret.
- Authenticate only the transport: rejected because authenticated peers can still have
  different repository roles.
- Give every mutation writer every lifecycle action: rejected because recovery
  acknowledgement and evidence access are materially more privileged.
- Freeze authorization at intake: rejected because transfer and role revocation must
  invalidate later lifecycle access.

## References

- Related docs: [architecture overview](../architecture-overview.md), [critical platform hardening design](../../openspec/changes/critical-platform-hardening/design.md)
