# Daemon Stage 2 — Workspace caching

- **Status:** Narrow read-only slice accepted; broader roadmap parked
- **Superseded for the current phase by:** [ADR-0008](../docs/adr/0008-daemon-transparent-passthrough-phase.md)
- **Governing changes:** [critical platform hardening OpenSpec](../openspec/changes/critical-platform-hardening/proposal.md) and [read-only workspace reuse](../openspec/changes/daemon-read-only-workspace-cache/proposal.md)

## Decision

Do not implement the broader daemon-owned workspace roadmap while the current hardening change is active. ADR-0017 permits only the bounded read-only lease cache for doctor, outline, describe, search_symbols, source, and relationships.

Outside that explicit slice the daemon remains an optional transparent typed transport adapter. The read-only cache does not own repository trust, mutation state, operation idempotency, retry policy, or command semantics, and it is structurally unavailable to mutation operations.

The earlier sketch based cache validity on solution path, Git HEAD, dirty state, and index modification time. That is not a sufficient semantic cache key. It can miss or mishandle:

- untracked and ignored source/configuration files;
- working-tree content changes that do not alter the index;
- linked files and files outside the solution directory;
- `Directory.Build.*`, `Directory.Packages.props`, `global.json`, NuGet configuration, imports, SDK/workload changes, and environment-dependent properties;
- generated files, source generators, analyzers, additional documents, and analyzer-config documents;
- configuration, target framework, runtime, and global property differences;
- repository trust/evaluator policy;
- filesystem watcher loss, rename races, timestamp granularity, and case/symlink identity;
- concurrent requests for multiple repositories or revisions; and
- workspace memory ownership, disposal, cancellation, and upgrade draining.

A fast stale semantic model is worse than a slower correct one, particularly when the result can drive deletion or refactoring.

## Preconditions for reconsideration

A new daemon-workspace proposal requires all of the following evidence first:

- [ ] The P0 mutation safety kernel is complete, including caller-known operation identity, durable status, rollback, and `RecoveryRequired`.
- [ ] Repository trust and isolated evaluation are implemented and tested.
- [ ] Workspace acquisition returns `Complete | Partial | Failed` with full provenance.
- [ ] The five-command canonical spine has no CLI/MCP compatibility bridges and passes direct/CLI/MCP/passthrough parity.
- [ ] MCP cancellation, paging, limits, and lifecycle behaviour are stable.
- [ ] Cold-load performance is measured on representative small, medium, and large solutions.
- [ ] Profiling shows workspace loading is the dominant remaining cost and that simpler in-process/session-scoped indexing is insufficient.
- [ ] A separately approved OpenSpec and ADR define daemon service ownership and failure behaviour.

## Required design for any future proposal

A future workspace daemon proposal must specify and test:

### Identity and cache key

- canonical repository, solution, configuration, target framework, SDK, global properties, trust mode, and evaluator identity;
- complete input fingerprint or sound invalidation model for source, projects, imports, packages, analyzers, generators, additional/config documents, and external linked files;
- case, symlink, and filesystem identity rules; and
- behaviour for dirty, untracked, ignored, generated, or externally modified files.

### Isolation and concurrency

- per-user access-controlled endpoint and repository authorisation;
- multiple repositories, revisions, and configurations concurrently;
- request scheduling, cancellation, fairness, and draining;
- no process-current-directory or global environment semantics; and
- safe evaluator-worker isolation for untrusted or constrained repositories.

### Lifetime and resources

- workspace/session leases and disposal;
- memory limits, LRU/expiry, pressure response, and large-solution behaviour;
- health, status, metrics, and deterministic diagnostic bundles;
- process restart, binary upgrade, protocol mismatch, and stale-client behaviour; and
- log redaction and retention.

### Correctness

- cache hit and invalidation proofs, including missed-watcher-event recovery;
- source revision exposed in every result;
- partial/failed load propagation and confidence downgrading;
- no stale model use for mutation preconditions; and
- parity with a clean direct load under adversarial change sequences.

## Reconsideration success criteria

Only proceed when a prototype demonstrates, on representative repositories:

- a material measured improvement to user-perceived latency or throughput;
- no semantic divergence from clean direct loads across the invalidation suite;
- bounded memory and predictable eviction;
- correct concurrent multi-repository behaviour;
- safe trust/evaluator isolation; and
- a simpler operational story than the performance problem it solves.

Until then, optimise canonical operations, request-scoped workspace use, indexing, cancellation, and output bounds before adding daemon state.
