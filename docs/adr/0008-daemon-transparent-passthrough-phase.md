# ADR-0008: Transparent daemon passthrough phase

- Status: Accepted
- Date: 2026-08-27
- Supersedes: the daemon-execution portions of [ADR-0005](0005-daemon-centred-local-execution.md) for the current hardening phase
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

The current daemon adds process, recovery, and global-state complexity before a canonical operation model and mutation safety kernel are proven. In particular, transport ambiguity must not cause a mutation replay or a semantically different in-process fallback. Workspace caching would introduce separate cache/session/invalidation correctness work and is not required to complete the shared application contract.

## Decision

For the current phase, the daemon is an optional **transparent transport adapter**. It forwards a typed canonical request to the same application operation used in-process and returns that operation's typed result unchanged.

The daemon SHALL NOT:

- cache or own workspaces, sessions, source revisions, or repository state;
- own mutation execution, idempotency records, or operation lifecycle semantics;
- retry, or cause a host to retry, after mutation dispatch is indeterminate;
- trigger in-process fallback after dispatch is indeterminate;
- reconstruct CLI arguments, render/capture CLI output, or contain command-specific semantic logic;
- use process current directory as request context; or
- change cancellation, diagnostics, safety classification, confidence, or provenance.

A host may select in-process execution only when the transport reports that the request was not dispatched. An indeterminate result is surfaced with the stable operation ID and status guidance. The transparent adapter must pass parity tests against direct in-process execution.

## Consequences

- Workspace caching and daemon-owned execution are explicitly parked.
- The canonical operation and mutation safety kernel become prerequisites, rather than daemon features.
- Existing daemon-centred statements are read through this temporary boundary; new daemon service work requires a separate approved OpenSpec change and ADR.
- If transparent operation cannot be demonstrated for a command, the command executes in-process.

## Future reconsideration

A real workspace daemon may be proposed only after the canonical spine and P0 mutation guarantees pass. Its proposal must separately specify session identity, revision/cache keys, invalidation, isolation, memory bounds, concurrency, health/status, idempotency ledger, cancellation, log redaction, and multi-repository behaviour.
