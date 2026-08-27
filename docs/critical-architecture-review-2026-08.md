# Critical architecture review — execution safety and agent contract

- Status: adopted as a planning baseline
- Reviewed baseline: `main` at `488888439ab9e75663d43c50db1b6f4e764f1960`
- Scope: static review of the CLI, MCP host, daemon, workspace loading, mutations, architecture docs, tests, CI, distribution, and representative analyses/refactorings. It is not a fault-injection or runtime reliability certification.

## Executive assessment

The product idea is unusually strong: compact semantic C# operations such as outline, describe, reference tracing, implementation discovery, and solution-wide refactoring can give people and coding agents evidence that raw text search cannot. The repository also has valuable strengths: dogfooding, conservative `find-unused` confidence work, deterministic command suggestion, strict build discipline, and unusually candid migration documentation.

The central problem is sequencing. The repository describes a safe, shared, typed platform, but important runtime invariants are still migration goals. Treat read-only semantic commands as productivity aids; treat mutations as review-first operations until the safety kernel is complete. Do not allow unattended MCP mutation application in the current state.

## Priorities

### P0 — block unsafe mutation claims and paths

1. **Indeterminate daemon dispatch can duplicate a mutation.** Recovery/fallback paths can resend a request after the daemon may already have applied it. A new request ID and no durable idempotency record make this a double-apply window.
2. **Mutation application is not failure-atomic.** Direct, sequential writes and follow-up filesystem moves can leave a partially modified tree. A rename can report applied even when an explicitly requested filesystem move failed.
3. **A partial workspace can produce proof-like deletion advice.** If some projects fail to load, missing references are invisible while diagnostics may be suppressed. A high-confidence unused result or safe-delete suggestion is not sound.

Until P0 is complete:

- mutation tools must default to review/dry-run;
- a daemon transport must never retry or fall back after indeterminate mutation dispatch;
- automatic deletion suggestions require a complete workspace;
- documentation must not imply transaction safety or exactly-once application.

### P1 — complete the platform spine before broad migration

1. **One canonical execution path.** CLI handlers, legacy argument-array daemon transport, typed RPC, direct MCP ports, an MCP CLI bridge, and in-process fallbacks have different cancellation, output, diagnostic, and mutation semantics.
2. **A mature agent/MCP contract.** The handwritten line-oriented server lacks stable structured schemas, lifecycle/cancellation/progress/pagination discipline, robust input limits, and protocol negotiation.
3. **Daemon complexity without current semantic value.** It adds global process state, cross-repository risk, serialisation, and recovery complexity before it owns a revision-aware workspace service.
4. **Metadata drift is a correctness defect.** Invalid `find-unused --scope` input can silently return empty results; `prime` overclaims suggestion availability. Agent-facing help is an executable contract.
5. **Analysis confidence and performance need a universal provenance model.** Confidence is promising in `find-unused`, but proof strength cannot be command-local.
6. **Command-surface, distribution, and CI gaps remain.** The command catalogue, schemas, examples, packaging, protocol matrix, and spawned-host tests need to be measured and gated.

## Target architecture

The target is a canonical typed operation model:

```text
CLI / MCP / future IDE adapters
  -> typed request binding and rendering only
  -> shared operation router
  -> typed application operation
  -> workspace/session boundary + safety policy
  -> services/core
```

Every command admitted to the stable surface has one typed request, one operation, one result/error envelope, one safety classification, and one provenance model. CLI, MCP, and the optional daemon transport project the same result; no host rebuilds CLI arguments or scrapes rendered text.

The initial vertical slice is intentionally small: `outline`, `find-refs`, `find-unused`, `rename`, and `safe-delete`. Delete legacy execution paths for each slice before fanning out to the remaining commands.

## Mutation safety kernel

Replace a boolean dry-run/apply model with explicit domain operations:

```text
Prepare(request) -> PreparedChangeSet
Apply(preparedChangeSetId, expectedRevision, idempotencyKey) -> ApplyResult
```

A prepared change set contains a stable plan ID, workspace/repository revision, expected hashes, all creates/updates/deletes/moves/project edits, an exact diff, diagnostics/assumptions, verification plan, and expiry rules. Apply preflights every condition, stages content, detects staleness, uses atomic per-file replacement where possible, journals multi-file rollback, and reports either complete success or complete rollback.

Transport classification is mandatory:

- `NotDispatched`: retry may be safe.
- `Completed`: a terminal result is known.
- `Indeterminate`: the caller must not retry, fall back, or claim failure; it must surface the operation ID and allow status lookup.

## Workspace completeness, provenance, and confidence

Workspace loading returns completeness (`complete | partial | failed`), requested/loaded/failed projects, diagnostics, solution revision, SDK/configuration, and source identity. Read-only operations may return partial data, but top-level status and every affected finding must say so. Partial workspaces cannot produce proof-strength findings, deletion advice, or automatic mutation suggestions.

Every structured response carries schema version, request ID, diagnostics, provenance, confidence level/basis/limitations, next actions, and pagination state. Confidence becomes a shared policy, not an analyser-specific adjective.

## AI and MCP contract

Expose a curated stable subset, classified as stable/preview/experimental. Stable MCP tools use real request and response schemas, structured content first, bounded input/output, cancellation, progress, pagination/field projection, lifecycle handling, and negotiated protocol support. Text is an additional human view. Never use a generic untyped `execute_command` bag; keep discoverable, constrained tools.

Agent primers and examples must be generated from the capability catalogue. CI executes every published example and asserts metadata, aliases, defaults, binding, schemas, and runtime behaviour agree.

## Command-surface and admission policy

Do not add commands while the vertical spine is incomplete. A new stable command requires:

- a typed request/result and a canonical operation;
- validated input (invalid values are errors, never empty success);
- declared safety, completeness, confidence, provenance, and cost limits;
- CLI/MCP/in-process parity and, if transported, daemon parity;
- cancellation and structured expected failures;
- review/dry-run/apply/no-op tests for mutations;
- executable help/examples and discoverable follow-on actions;
- coverage, fault-injection, and distribution gates proportional to impact.

## Daemon decision: transparent passthrough for now

The full daemon workspace-service implementation is explicitly parked. For this phase the daemon is only an optional transparent transport adapter over the same in-process canonical operation. It must have no workspace cache, revision/session ownership, retry after indeterminate mutation dispatch, mutation ownership, fallback that changes semantics, or command-specific semantic logic. It must preserve request ID, cancellation, typed result, diagnostics, and provenance exactly. If transparency cannot be demonstrated, execute in-process.

A future daemon service is a separate proposal, only after the safety kernel and canonical spine are proven.

## Staged remediation

1. Land this planning baseline and make safety claims accurate.
2. Implement workspace completeness/provenance and the prepared-change-set/idempotency kernel.
3. Build and test the five-command canonical vertical spine; remove its bridges.
4. Stabilise the MCP contract, metadata catalogue, examples, and agent primer.
5. Fan out command migrations in bounded groups only after the spine passes parity and fault-injection gates.
6. Improve distribution: SDK/runtime checks, portable install verification, protocol matrix, spawned-host integration tests, release smoke tests, and telemetry-free diagnostic bundles.
7. Reconsider a real daemon workspace service in a new, explicitly approved proposal.
