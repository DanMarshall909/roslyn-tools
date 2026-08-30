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
- trust state and evaluator mode;
- cancellation and resource budget; and
- client/host identity for diagnostics, not authorisation.

Operations do not read or mutate process current directory, global console state, `Environment.ExitCode`, or arbitrary process environment as request context.

## Trust and evaluator-isolation boundary

`WorkspaceRequest` declares two independent axes:

- `TrustState` is `Trusted` only under the user's explicit, repository-scoped decision; otherwise it is `Untrusted`.
- `EvaluatorMode` is `NoEvaluation`, `InProcess`, or `IsolatedWorker`.

`Untrusted` can be paired with `NoEvaluation` or `IsolatedWorker`, never
`InProcess`. `Trusted` can still be paired with `IsolatedWorker` when the user or
policy requests defence in depth. Isolation is therefore an execution decision, not
a trust state.

The isolated worker runs inside an OS-enforced allow-listed filesystem namespace.
The namespace exposes only the repository through a read-only mount or equivalent
handle boundary, explicitly declared read-only SDK/package/tool inputs, and separate
disposable writable build-output, intermediate, analyzer/package-cache, and
temporary locations outside the checkout. Host filesystem roots, user profiles,
SSH keys, credential stores, undeclared package caches, device endpoints, and other
ambient paths are not mounted or visible. The worker inherits no secrets, has
bounded CPU/memory/time, and has no network unless explicitly enabled.
Repository-controlled tasks, analyzers, or generators that attempt repository
writes or undeclared host reads are denied and audited; evaluator completion never
copies undeclared writes back into the checkout. If the platform cannot establish
this namespace, `IsolatedWorker` evaluation is refused.

The CLI, MCP host, and daemon are long-lived privileged processes and SHALL NOT evaluate an untrusted repository in-process. A trust decision is scoped to a canonical repository identity and can be inspected and revoked. It is never inferred merely because a checkout is local or an editor opened it.

Workspace provenance records:

- canonical repository and solution identity;
- trust state and evaluator mode;
- requested, loaded, and failed projects;
- SDK/MSBuild/compiler versions and configuration;
- relevant global.json and project graph identity;
- analyzer/generator execution policy;
- source revision and per-input hashes where required; and
- all load diagnostics, including those suppressed from a human text view.

## Prepared mutation protocol

### Identity

`Prepare` returns an immutable `PreparedChangeSet` with a stable `PlanId` and
`PlanHash`. Before prepare succeeds, the canonical serialized plan is atomically
persisted in a user-private durable plan store outside the target repository. The
store is logically scoped by canonical repository identity and survives CLI, MCP,
and daemon process restart. `Apply` receives both the plan identity and a
caller-generated `OperationId` before any transport dispatch:

```text
Prepare(request) -> PreparedChangeSet
Apply(repositoryId, planId, planHash, operationId, requestHash, expectedRevision, replayEpoch?) -> ApplyResult
GetOperation(repositoryId, operationId, requestHash) -> OperationStatus
Recover(operationId, recoveryAction) -> RecoveryResult
```

`OperationId` is both the idempotency and status-lookup handle. `RequestHash` is
the canonical hash of the complete apply request except transport-attempt and replay
fields. A transport request-attempt ID may be separate.

Before an accepted request can enter an execution queue, the receiver acquires a
per-operation intake fence and durably records `Received(OperationId, RequestHash,
CanonicalRequest)`. The canonical request is stored in the user-private control
plane, not daemon logs. The operation ledger is then created before side effects
begin. Startup and status lookup reconcile a `Received` receipt that has no operation
record, so accepted work cannot disappear between transport intake and ledger
creation.

If indeterminate dispatch is followed by a status lookup and neither a receipt nor
an operation exists, `GetOperation` acquires the same intake fence and first proves
that no receiver for that operation ID remains in flight. It then durably records
`NotRecorded(OperationId, RequestHash, ReplayEpoch)`. This is an authoritative
no-effect dispatch-resolution state, not an inference from a missing row. Any delayed
original attempt lacks that replay epoch and is rejected. The caller may explicitly
resubmit the exact canonical request with the same operation ID, request hash, and
replay epoch; one compare-and-swap transitions `NotRecorded` to `Received`. A lost
`NotRecorded` response can be queried again to recover the same epoch. This explicit
resolution is not an automatic retry or in-process fallback.

Reusing an `OperationId` with the same canonical request returns the existing receipt,
operation state, or result. Reusing it with a different plan, revision, request hash,
or payload returns `OperationConflict` and performs no work. `Received` and
`NotRecorded` records are bounded: they pin the referenced plan until promotion,
explicit replay, or durable expiry, and neither can remain unresolved indefinitely.

Apply loads the exact persisted plan bytes identified by repository ID, plan ID, and
plan hash. A missing, expired, unreadable, schema-incompatible, or hash-mismatched plan
is rejected before the operation ledger enters `Applying`. Apply never reconstructs a
plan from command arguments or a newer workspace snapshot. The plan store uses
owner-only access where supported, bounded retention for unreferenced plans and
safely cleaned terminal history, atomic writes, and crash-safe cleanup;
repositories cannot supply or replace its control metadata.

Bounded retention applies only to unreferenced prepared plans and safely cleaned
terminal history. Creating an operation record pins its canonical plan bytes by
durable ledger reference. Garbage collection cannot remove a plan while an
associated operation is `Received`, `NotRecorded`, `Prepared`, `Applying`,
`Reconciling`, `Recovering`, or `RecoveryRequired`, and
releases it only after verified terminal cleanup or explicit recovery
acknowledgement. Recovery evidence and its plan are retained as one referentially
complete bundle.

### Prepared plan contents

A prepared change set contains:

- plan ID, plan hash, schema version, creation time, expiry, repository identity, and expected repository revision;
- exact operation list for creates, replacements, deletes, moves, project edits, Roslyn documents, additional documents, and analyzer-config documents;
- expected existence and content hash for every read or modified path;
- expected file type, platform file identity, link count, known hard-link aliases, and symlink/hard-link policy;
- encoding, BOM, newline, and file-mode/permission preservation policy;
- root-containment proof for every source, destination, staging, and backup path;
- exact diff and machine-readable change model;
- diagnostics, assumptions, confidence, and irreversible limitations;
- verification steps and expected post-apply hashes; and
- cleanup and recovery policy.

Preparation performs no target-repository writes. Persisting the canonical plan in
the user-private control-plane store is part of preparation and must complete before
the plan is returned. Review and dry-run rendering are projections of those exact
persisted bytes, and apply consumes those bytes without recomputation.

### Apply state machine

```text
Prepared
  -> Applying
       -> Applied
       -> RolledBack
       -> Reconciling
            -> Applied
            -> RolledBack
            -> RecoveryRequired
       -> RecoveryRequired
  -> RejectedStale
  -> RejectedConflict
  -> Expired

Unknown
  -> Received
       -> Prepared
       -> RejectedStale
       -> RejectedConflict
       -> Expired
  -> NotRecorded
       -> Received  (same request hash plus replay epoch only)
       -> Expired

RecoveryRequired
  -> Recovering
       -> RecoveredApplied
       -> RecoveredRolledBack
       -> RecoveryRequired
       -> Reconciling
            -> RecoveredApplied
            -> RecoveredRolledBack
            -> RecoveryRequired
  -> RecoveryAcknowledged
```

The ledger stores a current state for lookup plus an append-only transition history.
No recovery action rewrites or erases the original `RecoveryRequired` event. Direct
apply outcomes use `Applied` and `RolledBack`; verified manual-recovery outcomes use
`RecoveredApplied` and `RecoveredRolledBack`. `RecoveryAcknowledged` records an
explicit administrative handoff and does not assert that repository contents are
clean.

The implementation:

1. loads and verifies the exact persisted plan;
2. creates or reads the durable operation record;
3. acquires an exclusive transaction reservation keyed by canonical repository identity, then validates replay semantics and claims a renewable owner lease plus fencing epoch;
4. while holding the reservation, performs final whole-plan preflight of every revision, read dependency, path, hash, permission, link, destination, and free-space condition;
5. stages replacement content, preferably on the same volume;
6. durably writes a journal and recovery metadata;
7. while continuously holding the reservation, revalidates ledger state, fencing epoch, lease, the expected intermediate repository state, all prior effect outcomes, and every still-relevant plan dependency before each operation, then journals intent, performs the side effect, and durably records its observed outcome;
8. orders those reserved operation boundaries deterministically and uses atomic per-path replacement where supported;
9. verifies expected postconditions;
10. commits the terminal result and cleans staging before releasing the reservation only after durable success; or
11. performs rollback from the journal.

Rollback is best-effort across platform/filesystem constraints and is not falsely described as atomic. Once side effects begin, cancellation requests are recorded but do not interrupt recovery-critical rollback. If rollback cannot restore every verified precondition, the operation enters the durable intervention state `RecoveryRequired`, not `RolledBack` or `Failed`. Journals, backups, affected paths, observed hashes, and deterministic recovery instructions are retained until successful recovery or explicit acknowledgement.

Stale-plan rejection is permitted only when drift is detected before the first
repository effect. If revision, dependency, path, identity, hash, permission, link,
or other declared precondition drifts after any effect has been journalled, the owner
records the observed drift, performs no remaining planned effect, and enters the
verified rollback path. Complete verified restoration ends as `RolledBack`; any
unrestored or ambiguous path ends as `RecoveryRequired`. A mid-apply drift is never
reported as `RejectedStale` or `Applied`.

### Manual recovery and acknowledgement

`RecoveryRequired` is not terminal. It is a durable recovery barrier for the
canonical repository: new apply operations are rejected before final validation
until the operation reaches a verified recovered state or an authorized user
explicitly acknowledges the unresolved condition.

`resume` and `rollback` acquire the same exclusive repository transaction
reservation, compare-and-swap the current state to `Recovering`, and claim a new
owner lease and fencing epoch before touching the repository. They operate only from
a declared journal boundary and revalidate the retained plan, evidence, paths, file
identities, and expected intermediate state at every effect. Successful resume ends
as `RecoveredApplied`; successful rollback ends as `RecoveredRolledBack`. A failed
or ambiguous recovery appends its attempt evidence and returns to
`RecoveryRequired`. An expired `Recovering` owner follows the same fenced,
reservation-protected reconciliation protocol as an expired `Applying` owner, while
preserving the requested recovery action in the journal.

`acknowledge` performs no repository effect. It appends `RecoveryAcknowledged` with
the actor, time, reason, affected paths, last observed hashes, unresolved warnings,
and an explicit statement that repository cleanliness was not verified. Only that
explicit action lifts the recovery barrier without verification; every later plan
must be prepared against the then-observed repository revision.

The exact plan, journal, backups, and recovery bundle remain pinned throughout
`RecoveryRequired`, `Recovering`, and recovery reconciliation. After
`RecoveredApplied` or `RecoveredRolledBack`, bulky artifacts are released only after
verified cleanup; after `RecoveryAcknowledged`, they are released only according to
the acknowledged retention choice. In every case an immutable transition and audit
summary remains for the configured terminal-history retention period.

### Filesystem containment at side-effect time

String normalisation and preflight checks are necessary but insufficient. Every
create, open, stage, replace, move, delete, backup, and restore is performed relative
to verified directory handles with no-follow/reparse-point-safe semantics where the
operating system provides them. The implementation revalidates the root handle,
parent-chain identity, final-component type, and declared source/destination identity
immediately before each side effect. A platform/filesystem combination that cannot
prove this boundary blocks unattended apply rather than relying on a vulnerable
check-then-open sequence.

If a symlink, junction, mount point, or other reparse component changes after
preflight, the operation writes nothing through that component. Before the first
side effect it returns stale-plan rejection; after side effects have begun it follows
the journalled rollback path and returns `RolledBack` or `RecoveryRequired` according
to the verified repository state. No recovery action follows an unverified path.

Preparation and every side-effect boundary also inspect platform file identity and
hard-link count. A multiply linked file has an explicit reviewed policy:

- `Reject` blocks unattended apply;
- `Preserve` is allowed only when every affected alias and shared-inode effect is
  identified, contained, and represented in the plan; or
- `BreakByReplacement` explicitly replaces the selected directory entry without an
  in-place content or metadata mutation of the shared inode.

If identity/link-count inspection is unsupported, an alias may be outside the
verified boundary, or the identity/count changes after review, unattended apply is
blocked or the operation follows stale-plan/verified-recovery handling. No content,
permission, or metadata operation may implicitly affect an undeclared hard-link
alias, and no replacement may silently sever a reviewed link relationship.

### Restart reconciliation

Every `Applying` ledger entry contains an owner instance ID, monotonically increasing
fencing epoch, lease expiry, heartbeat, and exclusive transaction-reservation
identity. A live owner renews its lease while applying or rolling back and holds the
OS-enforced reservation continuously from final whole-plan preflight through the
durable terminal result or explicit recovery handoff. Other apply/recovery requests
for the same canonical repository wait, cancel, or reject before final validation;
their operations cannot interleave.

On process startup and `GetOperation`, a reconciler must acquire that same
reservation before a compare-and-swap advances the epoch and transitions an expired
`Applying` entry to `Reconciling`; process exit releases the OS reservation. If an
expired owner still holds it, the reconciler performs no repository effect and does
not claim the epoch until the reservation is safely released. The supervisor applies
a bounded shutdown to the mutation worker. If it cannot prove worker exit and OS
reservation release, it revokes the ledger epoch, records `RecoveryRequired` with
the possibly in-flight journal boundary, and performs no reconciliation effect until
release is proven. After takeover, a resumed old owner cannot reacquire the
reservation with its stale token and must stop before journalling or performing
another side effect. Fault injection suspends owners before and after reservation,
epoch, and effect boundaries to prove this property.

The reconciler verifies the plan hash, journal, backups, staged files, affected-path
identities, and observed pre/post hashes. It then deterministically completes a
provably finished apply, resumes or rolls back only from a declared journal boundary,
or enters `RecoveryRequired` with retained evidence. An operation cannot remain
`Applying` indefinitely after its owner disappears, and ambiguous state is never
reported as `Applied` or silently replayed.

## Transport outcome

Transport reports what the caller can prove about dispatch:

- `NotDispatched`: no request bytes were accepted; an automatic retry may be permitted by operation policy.
- `Completed`: a valid terminal or current operation response was received.
- `Indeterminate`: dispatch may have occurred but no valid response is available.

`Indeterminate` never triggers an automatic mutation retry or in-process fallback.
The host surfaces the caller-known `OperationId` and queries `GetOperation`. A
`Received` receipt or operation state is reconciled in place. Only an authoritative,
durably recorded `NotRecorded` result permits explicit resubmission of the identical
request with the returned replay epoch; this continues the same operation identity
and fences the delayed original attempt. Read-only operations may have a separately
declared retry policy, but a retry still uses the same request context and operation
contract.

## Workspace completeness, provenance, and confidence

Before acquisition, the operation constructs a semantic-input manifest for its
declared scope. It includes every requested project and transitive project reference;
the selected target framework, configuration, and compilation context for each
project (or every applicable context when the request does not narrow them); source,
additional, analyzer-config, and generated documents; project, metadata, framework,
SDK, package, and imported build inputs; and every evaluator, source-generator, or
analyzer stage whose output the operation requires. Each entry records loaded,
resolved, failed, policy-blocked, or explicitly excluded state and the diagnostics
that justify it.

Workspace acquisition returns `Complete`, `Partial`, or `Failed`, that manifest,
requested/loaded/failed project identities, and all diagnostics. `Complete` means
every required manifest entry was loaded or resolved at the declared source revision
and every required evaluation/generator stage completed in the declared evaluator
mode without a completeness-impacting diagnostic. A deliberately excluded input is
compatible with `Complete` only when the operation contract declares it irrelevant
and validates that premise; otherwise exclusion is missing input.

`Partial` means at least one usable semantic scope exists but a required project,
target framework, configuration, reference, build input, document, generator output,
or operation-required analyzer stage is unavailable, unresolved, failed, or blocked.
`Failed` means no usable semantic model remains or the missing input prevents the
operation's minimum declared scope. Diagnostic severity alone does not determine
completeness: the loader records whether each diagnostic has no completeness impact,
degrades a required input, or makes the requested scope unusable. A partial result
remains explicitly partial at top level and for every finding affected by missing
input.

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
