# ADR-0008: Transparent daemon passthrough phase

- Status: Superseded in part by [ADR-0009](0009-authenticated-daemon-endpoints.md) for endpoint authentication, [ADR-0013](0013-fenced-plan-unavailability-resolution.md) for dispatch-plan resolution, and [ADR-0015](0015-operation-lifecycle-authorization.md) for lifecycle authorization
- Date: 2026-08-27
- Updated: 2026-08-30
- Supersedes: the daemon-execution portions of [ADR-0005](0005-daemon-centred-local-execution.md), the warm-workspace and fallback portions of [ADR-0006](0006-mcp-lazy-daemon-lifetime.md), and daemon placement guidance in [ADR-0007](0007-host-neutral-command-contracts.md) for the current hardening phase
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)
- Endpoint authentication: [ADR-0009](0009-authenticated-daemon-endpoints.md)
- Dispatch-plan resolution: [ADR-0013](0013-fenced-plan-unavailability-resolution.md)
- Operation lifecycle authorization: [ADR-0015](0015-operation-lifecycle-authorization.md)

## Context

The daemon adds process, recovery, protocol, logging, and global-state complexity before a canonical operation model and mutation safety kernel are proven. The current typed RPC recovery coordinator is a useful safety improvement for migrated commands, but the legacy CLI daemon path can still time out after dispatch and then execute the command in-process. That is unacceptable for apply-capable work.

Transport ambiguity must not cause a mutation replay or a semantically different fallback. A recovery identifier generated only by the server is also insufficient: when the response is lost, the caller loses that identifier with it. Workspace caching would introduce separate cache/session/invalidation correctness work and is not required to complete the shared application contract.

The existing daemon also relies on a global well-known endpoint and, in legacy paths, process current directory, console capture, environment exit state, and raw request logging. Those behaviours are incompatible with transparent multi-repository execution and safe handling of source or secret-bearing requests.

## Decision

For the current phase, the daemon is an optional **transparent typed transport adapter**. It forwards the canonical typed request to the same application operation used in-process and returns that operation's typed result unchanged.

### Caller-known identity

Before transport dispatch, the caller obtains a bounded repository-scoped
`OperationEpochToken` from the coordination authority and constructs a structured
`OperationId` from its authenticated monotonic epoch plus a caller-generated nonce.
The operation epoch is distinct from owner fencing epochs and from replay
authorization. The token is repository/principal-bound and is presented on apply.
Canonical repository ID plus operation ID is the composite durable
idempotency, operation-status, and recovery handle; the operation ID need only be
unique within that repository. Every status, recovery, acknowledgement, and
retention lookup carries both values, but the pair is not authorization. Under
ADR-0015 every lifecycle call also carries protected authenticated-principal context
and passes current action-scoped repository authorization before disclosure or state
change. A separate request-attempt ID may be used for protocol correlation.

When dispatch is indeterminate, the host returns the existing repository/operation identity and
status-lookup guidance. The execution authority durably records a typed intake
receipt before execution enqueue and operation state before repository side effects.
Status lookup reconciles a receipt without an operation record. If neither exists, it
requires and validates the original token and request identity, including the plan
reference. It then uses the same cross-process, store-enforced operation-intake fence
to exclude an in-flight receiver and atomically records authoritative `NotRecorded`
with a one-use replay epoch only if the operation epoch remains open. Only before that
epoch seals may the caller explicitly resubmit the exact request under the same
operation ID and replay epoch; delayed original attempts are fenced out. A different
payload under a live or retained operation ID is an operation-conflict response for
that incoming attempt; it cannot transition, terminate, or release resources owned by
the canonical operation.

The request hash uses the shared versioned semantic canonicalizer and includes the
structured operation ID plus `OperationEpochClaimsHash` over the token's canonical
public scope, epoch, limits, and `NotAfter`. It excludes the token signature/bearer
proof, `RequestHash` itself, replay epoch, cancellation state, other authentication
proof, and transport-only metadata. Daemon wire JSON is never the hash input.

The authority bounds open and unretired epochs, issuance rate, operations and bytes
per epoch, terminal-record bytes, replay duration, and pinned recovery/cleanup
exceptions. It seals an epoch at expiry or quota, atomically expires outstanding
`NotRecorded` records and their replay authorizations, and rejects unknown IDs from
sealed epochs without creating `NotRecorded` or authorizing replay. After the minimum replay window, it
atomically folds eligible terminal digests into a constant-size cumulative
`RetiredOperationAccumulator`, advances a monotonic `RetiredThroughEpoch` watermark,
and only then deletes their individual outcomes. Pinned live/recovery records remain
individually addressable and count against a hard admission cap. An otherwise unknown
ID at or below the watermark returns `OperationEpochRetired` and can never become
executable. Repository identity retirement permanently records the final watermark
and accumulator; repository IDs and operation epoch numbers are never reused.
Admission reserves the bounded ledger, terminal-outcome, and recovery-metadata space
an accepted operation may need, so quota exhaustion cannot prevent truthful state.

Retirement first enters `RetirementPending`, blocks new mutation and transfer, and
preserves status/recovery until every intake, owner, reservation, nonterminal,
recovery, cleanup, pinned, and retained-outcome record is resolved and compacted
through the final issued epoch. Only then may one transaction replace live routing
with the permanent marker. A pinned operation never loses its composite status key.

### Permitted fallback

A host may select in-process execution only when the transport proves `NotDispatched`. `Indeterminate` is surfaced unchanged and never triggers automatic mutation retry or in-process fallback. Explicit same-operation resubmission is allowed only after durable `NotRecorded` resolution, before the operation epoch seals, and requires its request hash and replay epoch. A sealed or retired epoch is non-replayable; a new attempt requires a current-epoch operation ID. Read-only retry behaviour must be explicitly declared and must still preserve the canonical request context and result contract.

Cancellation is `NotDispatched` only when no request bytes were accepted. After any
bytes are accepted, cancelling the response wait returns `Indeterminate` even if the
durable receipt is not yet visible. The caller keeps the same operation ID and uses
status resolution under the intake fence: `Received` wins and canonical cancellation
targets that operation, or authoritative `NotRecorded` wins and fences the delayed
receiver. An in-flight receiver is not inferred absent, and no new apply is issued.

### Process and endpoint isolation

The daemon carries absolute request context. It does not use or mutate process current directory, global console state, global exit code, or arbitrary environment as operation context.

The daemon registers one process-global MSBuild instance at startup and records its
normalized MSBuild path and version. Before invoking a typed request, it resolves the
instance selected by the effective solution directory and requires both identity
components to match. Mismatch or unresolved selection is a typed, fail-closed error
with `daemonRestartRequired` and both available identities; it never re-registers
MSBuild in-process. Different worktree paths are accepted only when selection resolves
to the same registered instance.
To keep that identity authoritative, daemon startup refuses unscoped defaults or a
manual registration fallback when no queryable SDK instance is available. This can
reduce daemon availability; the direct in-process host retains the existing fallback.

Its local endpoint is per-user, collision-resistant, and access controlled where the operating system permits. The global `rt-daemon` endpoint is a migration target, not the accepted design.

The daemon logs bounded structured metadata and redacted diagnostics. It does not persist raw JSON requests, command-line payloads, piped source/input, arbitrary environment values, or unbounded exception/source data.

### Cross-principal mutation coordination

A per-user daemon or ledger cannot define repository identity or hide an active
barrier. Every would-be writer first resolves an OS/service-protected cross-principal
and cross-host registry outside repository control that maps checkout/Git-common-dir
identity to one non-reused repository ID and records ownership, active recovery
barriers, and retirement. Machine-local state qualifies only when other hosts cannot
write the storage. A new checkout uses an explicit separately authorized enrollment
operation: the continuity-complete authority verifies handle-based physical identity,
fences concurrent enrollment, advances a protected monotonic repository sequence, and
atomically persists the complete identity/domain/no-barrier/audit record. Mutation
never enrolls implicitly. A healthy absent mapping returns `RepositoryNotEnrolled`;
missing, rolled-back, unreachable, or inconsistent authority state rejects enrollment
and mutation as `RejectedUnsafeCoordination`. Full operation data may remain per-user only when OS ownership/ACL
proof shows that no other non-administrative principal or host in the threat model
can write or replace the checkout and Git common directory. A shared writable
checkout requires a separate OS/service-enforced coordination authority that every
principal and adapter uses for intake, reservations, state, recovery barriers,
fencing, bounded operation-epoch retention, and retirement watermarks. If neither mode is proven, mutation is rejected and the
checkout remains inspect-only. This authority is a safety primitive of the canonical
application operation; it does not give the transparent daemon workspace, execution,
or mutation-policy ownership.

Enrollment is bounded authority-wide and per principal by live count/bytes,
rate/concurrency, and retired-binding index count/bytes. It reserves one fixed-size
retirement entry before advancing the repository sequence. Retirement consumes that
reservation, retains domain-separated keyed repository/binding digests in a
continuity-protected bounded index, and folds bulky final detail into a fixed-size
authenticated cumulative accumulator.
Capacity exhaustion returns `RepositoryCapacityExceeded` for new enrollment while
existing status/recovery/transfer/retirement remain available; the authority never
forgets a retired binding or grows the hot coordination store without limit.

Ownership transfer preserves repository identity and runs under an authority-wide
`TransferPending` fence. New intake stops while every live owner completes or is
fenced and reconciled under the existing reservation only after its effect fence is
closed/drained or worker termination and write-capability closure are verified.
`OwnerQuiescencePending` is not acknowledgeable; other barriers must be recovered or
acknowledged and all active state proven quiescent before one transaction increments
the coordination generation, changes the writer domain, and rotates credentials. A
crash exposes old ownership, completed new ownership, or the durable transfer fence,
never two writable domains.

### Prohibited ownership

The daemon SHALL NOT:

- cache or own workspaces, sessions, source revisions, repository trust, or repository state;
- evaluate an untrusted repository inside the long-lived daemon process;
- own mutation preparation, apply semantics, idempotency records, or operation lifecycle policy;
- retry, or cause a host to retry, after mutation dispatch is indeterminate;
- trigger in-process fallback after mutation dispatch is indeterminate;
- route apply-capable commands through the legacy CLI argument-array protocol;
- reconstruct CLI arguments, render/capture CLI output, or contain command-specific semantic logic;
- use process current directory, global environment/exit state, or console replacement as request context;
- expose one cross-user global endpoint; or
- change cancellation, diagnostics, safety classification, confidence, provenance, paging, or resource policy.

The transparent adapter must pass parity and failure-injection tests against direct in-process execution. If transparency cannot be demonstrated for a command, that command executes in-process. If a command cannot prove safe recovery, it is review-only and is not exposed as a stable apply-capable MCP tool.

## Consequences

- Workspace caching and daemon-owned semantic execution are explicitly parked.
- The canonical operation, trust-aware workspace boundary, and mutation safety kernel become prerequisites rather than daemon features.
- The current typed recovery coordinator is retained as scaffolding but is not considered completion until the operation ledger and caller-known identity exist.
- Legacy daemon routing is removed first from every mutation, then command by command from the canonical spine.
- A lost response becomes a recoverable status query, not a reason to rerun work.
- Daemon startup cost may remain with no warm-workspace performance benefit during this phase. That is accepted to avoid premature cache correctness and isolation complexity.
- Active migration guidance now marks apply-capable commands `InProcess` and
  restricts `DaemonPreferred` to eligible read-only typed operations. Superseded ADRs
  are status-marked and cross-linked here rather than treated as concurrent policy.
- New daemon service work requires a separate approved OpenSpec change and ADR.

## Required evidence

Before the passthrough daemon is considered stable:

- migrated CLI, MCP, daemon, and direct execution return equivalent typed outcomes;
- lost-response tests prove no second mutation dispatch;
- operation status survives client and daemon restart;
- concurrent repositories cannot affect one another through process-global state;
- endpoint isolation is tested for concurrent users/processes where supported;
- logs are tested for source, path, piped-input, environment, and secret redaction; and
- apply-capable legacy bridge commands are absent from daemon routing and stable MCP discovery.

## Future reconsideration

A real workspace daemon may be proposed only after the canonical spine, repository-trust boundary, and P0 mutation guarantees pass. Its proposal must separately specify:

- session and repository identity;
- revision/cache keys and invalidation;
- trust and evaluator isolation;
- memory and concurrency bounds;
- operation-ledger authority;
- health/status and upgrade draining;
- cancellation and request scheduling;
- access control and endpoint discovery;
- log redaction and retention; and
- multi-repository and multi-user behaviour.
