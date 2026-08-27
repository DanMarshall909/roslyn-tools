# Design: critical platform hardening

## Design principles

1. **One semantic operation, many adapters.** Hosts bind and render; they do not reconstruct commands, scrape output, or invent safety semantics.
2. **Repository input is not automatically trusted.** Loading a solution can evaluate repository-controlled build logic. Trust and isolation are explicit parts of workspace acquisition.
3. **A mutation is a recoverable operation, not a sequence of writes.** Preparation, review, application, verification, rollback, and recovery have stable identities and states.
4. **Transport knowledge and operation state are separate.** A lost response is `Indeterminate` transport knowledge, not evidence that an operation failed.
5. **Proof claims carry their premises.** Workspace completeness, source identity, analysis limitations, and confidence basis travel with every result.
6. **Builds and releases are hermetic.** Compilation does not modify the checkout or user profile, and released version claims are generated from one source.

## Canonical operation

Hosts bind input into a typed request and call one application operation. The operation owns validation, trust-aware workspace acquisition, cancellation, safety policy, and service orchestration. Hosts only project the typed outcome.

```text
CLI / MCP / future IDE adapter
        |
        | bind typed request + request context
        v
shared operation router
        |
        v
application operation
        |
        +--> trust-aware workspace acquisition
        +--> safety / confidence / resource policy
        +--> domain services and Roslyn core
        |
        v
shared typed result/error/provenance envelope
        |
        +--> host renderer
        +--> optional transparent daemon transport
```

Every stable command has one:

- canonical command identity;
- typed request and validation contract;
- typed operation;
- typed result/error envelope;
- safety and resource classification;
- provenance and confidence policy; and
- set of executable examples.

The compatibility bridge is temporary. It is removed command by command as each spine command reaches parity. No apply-capable bridge is admitted to the stable MCP surface.

## Request context and process isolation

A canonical request context contains absolute, normalised values rather than ambient process state:

- request ID and, for apply, caller-generated operation ID;
- repository root and solution path;
- working directory only as an explicit absolute path;
- configuration, target framework selection, SDK identity, and environment allow-list;
- trust mode and evaluator mode;
- cancellation and resource budget; and
- client/host identity for diagnostics, not authorisation.

Operations do not read or mutate process current directory, global console state, `Environment.ExitCode`, or arbitrary process environment as request context.

## Trusted-workspace boundary

`WorkspaceRequest` declares one of these trust modes:

- `Trusted`: repository-controlled evaluation may run under the user's explicit trust decision.
- `Isolated`: evaluation runs in a constrained worker with a narrow filesystem view, no inherited secrets, bounded CPU/memory/time, and no network unless explicitly enabled.
- `Untrusted`: project evaluation is refused; file-only operations may still run if their contract does not invoke MSBuild, analyzers, generators, or repository executables.

The CLI, MCP host, and daemon are long-lived privileged processes and SHALL NOT evaluate an untrusted repository in-process. A trust decision is scoped to a canonical repository identity and can be inspected and revoked. It is never inferred merely because a checkout is local or an editor opened it.

Workspace provenance records:

- canonical repository and solution identity;
- trust and evaluator mode;
- requested, loaded, and failed projects;
- SDK/MSBuild/compiler versions and configuration;
- relevant global.json and project graph identity;
- analyzer/generator execution policy;
- source revision and per-input hashes where required; and
- all load diagnostics, including those suppressed from a human text view.

## Prepared mutation protocol

### Identity

`Prepare` returns an immutable `PreparedChangeSet` with a stable `PlanId`. `Apply` receives a caller-generated `OperationId` before any transport dispatch:

```text
Prepare(request) -> PreparedChangeSet
Apply(planId, operationId, expectedRevision) -> ApplyResult
GetOperation(operationId) -> OperationStatus
Recover(operationId, recoveryAction) -> RecoveryResult
```

`OperationId` is both the idempotency and status-lookup handle. A transport request-attempt ID may be separate. The operation ledger is durably written before side effects begin.

Reusing an `OperationId` with the same canonical request returns the existing operation state/result. Reusing it with a different plan, revision, or payload returns `OperationConflict` and performs no work.

### Prepared plan contents

A prepared change set contains:

- plan ID, creation time, expiry, repository identity, and expected repository revision;
- exact operation list for creates, replacements, deletes, moves, project edits, Roslyn documents, additional documents, and analyzer-config documents;
- expected existence and content hash for every read or modified path;
- expected file type and symlink policy;
- encoding, BOM, newline, and file-mode/permission preservation policy;
- root-containment proof for every source, destination, staging, and backup path;
- exact diff and machine-readable change model;
- diagnostics, assumptions, confidence, and irreversible limitations;
- verification steps and expected post-apply hashes; and
- cleanup and recovery policy.

Preparation performs no repository writes. Review and dry-run rendering are projections of the same immutable plan that apply consumes.

### Apply state machine

```text
Prepared
  -> Applying
       -> Applied
       -> RolledBack
       -> RecoveryRequired
  -> RejectedStale
  -> RejectedConflict
  -> Expired
```

The implementation:

1. creates or reads the durable operation record;
2. validates operation replay semantics;
3. preflights every revision, path, hash, permission, symlink, destination, and free-space condition;
4. stages replacement content, preferably on the same volume;
5. durably writes a journal and recovery metadata;
6. applies operations in a deterministic order using atomic per-path replacement where supported;
7. verifies expected postconditions;
8. commits the terminal result and cleans staging only after durable success; or
9. performs rollback from the journal.

Rollback is best-effort across platform/filesystem constraints and is not falsely described as atomic. Once side effects begin, cancellation requests are recorded but do not interrupt recovery-critical rollback. If rollback cannot restore every verified precondition, the terminal state is `RecoveryRequired`, not `RolledBack` or `Failed`. Journals, backups, affected paths, observed hashes, and deterministic recovery instructions are retained until successful recovery or explicit acknowledgement.

## Transport outcome

Transport reports what the caller can prove about dispatch:

- `NotDispatched`: no request bytes were accepted; an automatic retry may be permitted by operation policy.
- `Completed`: a valid terminal or current operation response was received.
- `Indeterminate`: dispatch may have occurred but no valid response is available.

`Indeterminate` never triggers an automatic mutation retry or in-process fallback. The host surfaces the caller-known `OperationId` and queries `GetOperation`. Read-only operations may have a separately declared retry policy, but a retry still uses the same request context and operation contract.

## Workspace completeness, provenance, and confidence

Workspace acquisition returns `Complete`, `Partial`, or `Failed`, with requested/loaded/failed project identities and all diagnostics. A partial result remains explicitly partial at top level and for every finding affected by missing input.

Shared policy enforces:

- partial workspaces cannot produce proof confidence, automatic deletion advice, or mutation suggestions;
- externally visible symbols are not considered unused under a closed-world proof unless the operation declares and verifies that boundary;
- confidence includes `level`, `basis`, `limitations`, and `evidence` rather than an analyser-local adjective;
- result envelopes include schema version, request ID, status, typed data, diagnostics, provenance, confidence, next actions, and paging state; and
- invalid option values are structured `InvalidInput` errors, never empty successful result sets.

## MCP and agent surface

The preferred implementation is the official C# MCP SDK. A handwritten host is acceptable only if spawned-process conformance tests prove every advertised protocol version and capability.

Stable MCP tools:

- negotiate or explicitly select a supported protocol version;
- obey request/notification rules and never answer notifications;
- support cancellation for in-flight work and progress for long-running operations;
- expose schema-valid `structuredContent` plus an optional human text view;
- use complete JSON Schema for enums, bounds, nullability, nested values, and additional-property policy;
- bound request size, output size, item count, execution time, concurrency, and memory-sensitive operations;
- paginate or project large results deterministically;
- classify tools as `stable`, `preview`, or `experimental`; and
- hide apply-capable bridge tools from the stable surface.

Capability metadata generates or verifies CLI help, MCP schemas, `prime`, aliases/defaults, safety, examples, and follow-on actions. CI executes every published example against fixtures.

## Transparent daemon boundary

For this phase the daemon is optional typed transport only. It:

- accepts the same typed request the in-process router accepts;
- invokes the same operation and returns the same typed envelope;
- uses a per-user, collision-resistant, access-controlled endpoint;
- preserves request/operation IDs, cancellation, diagnostics, provenance, confidence, and safety state; and
- may be bypassed before dispatch when unavailable.

It has no workspace cache, global current-directory semantics, command-line argument bridge for mutations, output capture, idempotency authority, retry policy, or command-specific semantic mapping. It does not log raw request payloads; structured logs use redacted fields and bounded diagnostics.

A future workspace daemon requires a separately approved change covering session/cache identity, invalidation, isolation, memory limits, concurrency, operation ledger ownership, health/status APIs, upgrade draining, and multi-repository behaviour.

## Hermetic build and release design

`dotnet build` only restores, compiles, analyses, and writes normal build outputs. Repository document generation, formatting, skill deployment, mutation testing, installation, and dogfood analysis are explicit commands or opt-in targets.

Version is derived from one release source and projected into assemblies, NuGet packages, MCP metadata, archives, and server manifests. Pre-release builds do not claim a stable `1.0.0` contract.

Release verification:

- builds and runs supported RID artifacts on their native operating systems;
- starts the CLI and spawned MCP host from the packaged archive;
- exercises a fixture workspace and the protocol compatibility matrix;
- installs, upgrades, rolls back, and uninstalls without deleting unowned files;
- verifies checksums and provenance attestations;
- publishes the documented NuGet tool or removes that installation path; and
- records test evidence and residual risks in release notes.
