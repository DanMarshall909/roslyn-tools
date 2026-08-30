# ADR-0008: Transparent daemon passthrough phase

- Status: Accepted
- Date: 2026-08-27
- Updated: 2026-08-30
- Supersedes: the daemon-execution portions of [ADR-0005](0005-daemon-centred-local-execution.md), the warm-workspace and fallback portions of [ADR-0006](0006-mcp-lazy-daemon-lifetime.md), and daemon placement guidance in [ADR-0007](0007-host-neutral-command-contracts.md) for the current hardening phase
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

The daemon adds process, recovery, protocol, logging, and global-state complexity before a canonical operation model and mutation safety kernel are proven. The current typed RPC recovery coordinator is a useful safety improvement for migrated commands, but the legacy CLI daemon path can still time out after dispatch and then execute the command in-process. That is unacceptable for apply-capable work.

Transport ambiguity must not cause a mutation replay or a semantically different fallback. A recovery identifier generated only by the server is also insufficient: when the response is lost, the caller loses that identifier with it. Workspace caching would introduce separate cache/session/invalidation correctness work and is not required to complete the shared application contract.

The existing daemon also relies on a global well-known endpoint and, in legacy paths, process current directory, console capture, environment exit state, and raw request logging. Those behaviours are incompatible with transparent multi-repository execution and safe handling of source or secret-bearing requests.

## Decision

For the current phase, the daemon is an optional **transparent typed transport adapter**. It forwards the canonical typed request to the same application operation used in-process and returns that operation's typed result unchanged.

### Caller-known identity

Every apply request receives a caller-generated `OperationId` before transport dispatch. That identifier is the durable idempotency and operation-status handle. A separate request-attempt ID may be used for protocol correlation.

When dispatch is indeterminate, the host returns the existing operation ID and status-lookup guidance. It does not create or dispatch another apply operation. The execution authority records operation state before repository side effects and returns the existing result for an identical replay; a different payload under the same operation ID is an operation conflict.

### Permitted fallback

A host may select in-process execution only when the transport proves `NotDispatched`. `Indeterminate` is surfaced unchanged and never triggers mutation retry or in-process fallback. Read-only retry behaviour must be explicitly declared and must still preserve the canonical request context and result contract.

### Process and endpoint isolation

The daemon carries absolute request context. It does not use or mutate process current directory, global console state, global exit code, or arbitrary environment as operation context.

Its local endpoint is per-user, collision-resistant, and access controlled where the operating system permits. The global `rt-daemon` endpoint is a migration target, not the accepted design.

The daemon logs bounded structured metadata and redacted diagnostics. It does not persist raw JSON requests, command-line payloads, piped source/input, arbitrary environment values, or unbounded exception/source data.

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
