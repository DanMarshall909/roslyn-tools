# Architecture overview

This is the umbrella description of the `rt` target architecture. It names the layer boundaries, execution and safety contracts, output model, capability catalogue, repository-trust boundary, mutation protocol, quality gates, and dogfood loop. Update it whenever an architectural decision changes one of those contracts, alongside the relevant ADR.

Companion documents:

- [critical-architecture-review-2026-08.md](critical-architecture-review-2026-08.md) — critical baseline, evidence, risks, and remediation order.
- [critical platform hardening OpenSpec](../openspec/changes/critical-platform-hardening/proposal.md) — executable P0/P1 requirements and tasks.
- [pattern-core-service.md](pattern-core-service.md) — the canonical Core-service + thin-host-adapters pattern with worked examples.
- [target-command-surface-architecture.md](target-command-surface-architecture.md) — target shape for command wiring, catalogues, capabilities, and binders.
- [consistency-roadmap.md](consistency-roadmap.md) — execution plan sequencing the command-surface migrations.
- [command-application-port-migration.md](command-application-port-migration.md) — explicit conversion instructions for the complete command catalogue.
- [command-port-migration-groups.tsv](command-port-migration-groups.tsv) — machine-readable migration manifest.
- [per-command-checklist.md](per-command-checklist.md) — command-level admission checklist.
- [adr/](adr/) — architecture decision records.

## Architectural position

`rt` is a semantic C# operation engine with multiple host adapters. The CLI is not the application layer, the MCP server is not a second implementation, and the daemon is not the owner of truth.

The target shape is:

```text
┌─ Host adapters ─────────────────────────────────────────────────────┐
│ CLI: bind arguments + render terminal output                        │
│ MCP: protocol lifecycle + bind JSON + project typed result          │
│ IDE: editor context + native UX (future)                             │
└──────────────────────────────┬───────────────────────────────────────┘
                               │ typed request + explicit request context
                               ▼
┌─ Shared operation router / application definitions ────────────────┐
│ identity, request/result types, validation, safety, maturity, limits │
│ follow-ons, placement policy, provenance/confidence policy           │
└──────────────────────────────┬───────────────────────────────────────┘
                               ▼
┌─ Application operations ────────────────────────────────────────────┐
│ orchestration, cancellation, trust-aware workspace acquisition       │
│ mutation prepare/apply policy, expected failures, resource budgets   │
└──────────────┬──────────────────────────────────────┬────────────────┘
               ▼                                      ▼
┌─ Workspace boundary ─────────────────┐  ┌─ Domain services / Core ──┐
│ complete / partial / failed          │  │ Roslyn analysis/refactoring│
│ trust: trusted / untrusted           │  │ symbol resolution           │
│ evaluator: no-eval / in-proc / worker│  │                              │
│ revision + diagnostics + provenance  │  │ deterministic transformations│
└──────────────────────────────────────┘  └─────────────────────────────┘
                               │
                               ▼
┌─ Shared typed result/error/provenance envelope ─────────────────────┐
└──────────────────────────────┬───────────────────────────────────────┘
                               ▼
                     host-specific projection

Optional daemon transport sits around the typed request/result boundary only. It uses
an authenticated per-user endpoint or remains disabled; endpoint-name secrecy is not
authorization. It is not an additional semantic layer and does not own workspaces or
mutations.
```

**Placement rule:** could the behaviour be tested without CLI, MCP, daemon, console, or process-global state in scope? If not, host concerns have leaked inward.

## Contracts that bind the layers

### 1. Host-neutral application definition and port

Every ordinary stable command has one host-neutral application definition and exactly one typed `ICommandApplicationPort`. The definition carries:

- canonical identity and aliases;
- request and response types plus alternative request variants;
- `RtCommandInfo` and examples;
- visibility and maturity (`stable`, `preview`, or `experimental`);
- safety and execution placement;
- resource limits and paging policy;
- provenance and confidence policy; and
- suggested follow-on tools.

It contains no CLI builder, parser object, protocol type, renderer, console call, or daemon-specific semantics. `IRtCommand` is the CLI projection; the MCP adapter projects the same definition. During migration only, descriptors without complete ports enter an isolated compatibility bridge. Apply-capable bridge commands are not part of the stable MCP or daemon surface.

See [ADR-0003](adr/0003-shared-command-descriptors-and-application-ports.md), [ADR-0007](adr/0007-host-neutral-command-contracts.md), [ADR-0010](adr/0010-revision-bound-continuation-tokens.md), and the [migration playbook](command-application-port-migration.md).

### 2. Explicit request context

Every operation receives explicit, absolute request context:

- request ID and, for apply, a caller-known structured operation ID from a bounded authority-issued epoch;
- repository root and solution path;
- configuration and SDK/target-framework selection;
- requested trust constraint, authority-derived effective repository trust, and evaluator mode/policy;
- cancellation and resource budget; and
- source/revision identity where applicable.

Operations do not infer repository identity from process current directory and do not use global console, `Environment.ExitCode`, or arbitrary process environment as mutable request state. This is required for concurrent repositories, host parity, reproducibility, and future isolation.

### 3. Repository trust and workspace completeness

Loading an MSBuild solution can evaluate repository-controlled build logic and can involve analyzers or generators. A local checkout is not automatically trusted.

Effective trust comes from the OS/service-protected authenticated-principal-scoped
registry defined by [ADR-0011](adr/0011-authoritative-trust-and-evaluator-isolation.md).
It binds canonical repository identity to verified physical identity. A request trust
value can constrain or assert an expected state but cannot elevate it. Only deliberately
invoked authenticated grant/revoke operations through a trusted surface change the
audited record; absent, revoked, unreadable, mismatched, stale-binding, or
non-interactively bootstrapped decisions are `Untrusted`.

Grant, revoke, and `InProcess` evaluator admission share a linearizable authority gate
for the principal and verified repository binding. Admission atomically revalidates
the grant and acquires a revocable lease for the current monotonic trust generation
plus a held verified-root capability before repository-controlled code can run. The
actual solution/build open consumes its handle-relative or immutable snapshot view,
never a re-resolved ambient path; an evaluator that cannot preserve the physical
binding through startup is refused in-process. Revocation closes admission, advances
the generation, persists `Untrusted`, and cancels or terminates old-generation evaluations
and descendants. It reports completion only after verified quiescence; otherwise its
durable state is `RevocationPending`, new in-process admissions and replacement grants
remain fenced, and missing lease state fails closed into reconciliation.

Every in-process evaluator host and process it can create is placed before admission in
a non-breakaway OS descendant domain owned by an external supervisor that executes no
repository code. Repository code cannot detach, re-parent, disable, or escape it. On
revocation the supervisor cancels cooperatively, then terminates the per-admission
domain or the whole containing host if attribution is not provable; durable revocation
status completes only after the OS reports the revoked generation empty. Platforms
without non-escape, external kill, and empty-domain proof refuse `InProcess`.

Workspace acquisition and provenance carry two independent values. Effective
repository trust is **Trusted** after an authoritative explicit scoped decision or
**Untrusted** when that decision is absent or revoked. Evaluator mode is one of:

- **NoEvaluation** — only operations proven not to invoke repository-controlled code may run; for untrusted repositories, every host read also uses the root-contained safe-reader boundary below.
- **InProcess** — repository-controlled evaluation may run in the request host and is valid only for a trusted repository.
- **IsolatedWorker** — evaluation runs in a constrained worker even when the repository is trusted.

An untrusted repository can use `NoEvaluation` or `IsolatedWorker`, never
`InProcess`. A trusted repository may still request `IsolatedWorker`; choosing
isolation does not change or imply its trust state. The isolated worker runs under a
distinct least-privilege identity with OS-enforced process/PID, process-handle, and IPC
isolation. It cannot impersonate the host user, inspect/control host processes, reach
undeclared IPC, or inherit ambient handles; only bounded schema-validated
operation-scoped supervisor channels are available and every child remains contained.
The worker also receives an OS-enforced allow-listed filesystem namespace containing
only the read-only repository, declared read-only SDK/package/tool inputs, and separate
disposable writable output, cache, and temporary locations outside the checkout. Host roots,
user profiles, credentials, SSH material, undeclared package caches, and devices are
not visible. Checkout inputs are opened relative to the verified root with no-follow
and final-handle identity/link checks; unproven multiply linked files are rejected
before reading, and accepted content uses private non-host-aliasing inodes or an
equivalently proven immutable alias-contained mount. The worker has bounded resources,
no inherited secrets, and no network
during any phase that executes untrusted repository code. Required packages and tools
are acquired beforehand by a separate trusted broker that executes no repository code,
accepts only reviewed lockfile-pinned coordinates from allow-listed registries, and
verifies hashes/signatures before mounting them read-only. Missing inputs never trigger
restore inside the worker. Repository write attempts, undeclared host reads, and all
network egress are denied and recorded; platforms that cannot prove identity,
process/handle/IPC, filesystem, and egress isolation refuse untrusted evaluation.
Admission also reserves aggregate host capacity. Descendant-inherited OS limits cap
process/task and thread creation, process-table entries, handles/descriptors, CPU,
memory, time, and IPC/result bytes, while disposable filesystems enforce byte and
inode/file-count quotas. Every terminal or reconciliation path performs bounded
descendant termination and writable-namespace teardown; unverified cleanup quarantines
the namespace and its capacity rather than making either reusable.

Long-lived CLI, MCP, and daemon hosts never evaluate an untrusted repository in-process.

They also never treat `NoEvaluation` as permission for ambient reads. Untrusted
file-only operations use a shared handle-relative reader rooted at the verified
checkout, with no-follow/reparse-safe component opens, post-open filesystem identity
checks, declared regular-file/directory inputs, and conservative hard-link handling.
Symlinks, junctions, mounts, reparse points, alternate streams, special files, or
unproven external aliases are denied; if the boundary is unavailable, the operation
runs in the allow-listed worker or is refused. Redirected content cannot reach
results, diagnostics, logs, or caches.

Each workspace request declares a semantic-input manifest: requested projects and
transitive references; selected target frameworks, configurations, and compilation
contexts; source/additional/analyzer-config/generated documents; project, metadata,
framework, SDK, package, and imported build inputs; and operation-required evaluator,
generator, or analyzer stages. Acquisition records each entry as loaded/resolved,
failed, policy-blocked, or explicitly excluded, with its diagnostic completeness
impact.

Workspace acquisition is **Complete** only when every required manifest input is
available at the declared revision and every required stage succeeds in the declared
evaluator mode. An exclusion counts as complete only under a validated
operation-specific irrelevance premise. It is **Partial** when usable semantic scope
remains but any required input is missing, unresolved, failed, or blocked, and
**Failed** when no usable model or minimum declared scope remains. Results also carry
requested/loaded/failed projects, all diagnostics, SDK/compiler/configuration, trust
state/evaluator mode, source identity, and revision. Suppressing warnings from a
human renderer never removes the manifest, diagnostics, or completeness decision
from structured provenance.

Partial workspaces cannot produce proof-strength conclusions, automatic deletion advice, or mutation suggestions. Confidence is a shared structure with level, basis, evidence, and limitations—not an analyser-local adjective.

### 4. Category and capability contracts

A command implements exactly one category marker:

- `IInspectCommand` — read-only, focused inspection;
- `IAnalyzeCommand` — read-only, broader analysis; or
- `IRefactorCommand` — prepares or applies source changes.

Reusable options are capabilities backed by a central catalogue and shared binders, for example `IHasSolutionOption`, `ISupportsDryRun`, and `IHasSymbolTarget`. Services receive domain requests, not parser types. Known flags are not recreated command by command.

The current capability migration is intentionally incremental, but no new command may deepen the ad hoc option model.

### 5. Result and error contract

Two types serve different layers:

- **`Result<T, RtError>`** represents routine expected application outcomes such as symbol-not-found, ambiguous match, invalid input, stale plan, or operation conflict.
- **`RtResult<T>`** is the host-rendering envelope for status, diagnostics, and exit behaviour.

Expected failures are typed and total. Host adapters project them through shared presenters. Raw exception text, stack traces, and host-specific error ladders are not the application contract.

Every structured operation response carries, where applicable:

- schema version and request ID;
- canonical repository ID, operation ID, and operation/transport status;
- typed data;
- typed diagnostics;
- workspace and source provenance;
- confidence and limitations;
- safety state and affected paths;
- next actions; and
- paging/continuation state.

Continuation state is opaque, integrity protected, and bound to one canonical request,
repository/workspace identity, source and semantic-input revision, projection/filter/
total-order policy, cursor, authorization context, and bounded expiry under
[ADR-0010](adr/0010-revision-bound-continuation-tokens.md). Each page is reauthorized
and comes from the exact quota-bounded retained snapshot, or fails with a typed stale,
expired, invalid, or unauthorized continuation error. No page silently reevaluates a
newer checkout under an older cursor.

### 6. Mutation prepare/apply/recover contract

A boolean `dryRun` branch is not a mutation safety model. Stable mutations use:

```text
EnrollRepository(enrollmentRequest) -> RepositoryIdentity
Prepare(request) -> PreparedChangeSet
GetOperationEpoch(repositoryId) -> OperationEpochToken
Apply(repositoryId, planId, planHash, operationEpochToken, operationId, requestHash, expectedRevision, replayEpoch?) -> ApplyResult
GetOperation(repositoryId, operationId, requestIdentity?, operationEpochToken?) -> OperationStatus
Recover(repositoryId, operationId, action) -> RecoveryResult
```

Until the durable mutation kernel and its failure/recovery gates pass,
[ADR-0012](adr/0012-review-first-mutation-defaults.md) requires every apply-capable
command to default to non-writing `Review`, or `DryRun` when review-only analysis is
not meaningful. `Apply` requires explicit caller selection and remains in-process.
Existing `apply-default` catalogue entries are containment debt, not a migration
target; port conversion alone cannot preserve or justify them.

Repository identity and mutation coordination span OS principals and hosts. An OS-
or service-protected registry outside repository control maps checkout/Git-common-dir filesystem identity
to one non-reused repository ID and records mode, owner/authority, active recovery
barrier, and retirement state; no per-user store can shadow it. Every would-be
mutation principal/host resolves this registry before intake; machine-local state is
valid only when other hosts cannot write the filesystem. First-time enrollment is an
explicit authorized operation, never an apply side effect: under a handle-derived
physical-identity fence, a continuity-complete registry atomically advances its
protected authority-wide repository sequence and persists the complete binding,
coordination mode, initial no-barrier state, and audit entry. Concurrent enrollment
returns the same record; crash-created sequence gaps are allowed but reuse is not. A
coordination authority itself is provisioned separately by an administrator in a
demonstrably fresh OS/service namespace with a non-rollback continuity anchor;
enrollment cannot reprovision it. A
healthy absent mapping returns `RepositoryNotEnrolled`; missing, inconsistent,
rolled-back, or unreachable authority state blocks both enrollment and mutation as
`RejectedUnsafeCoordination` instead of treating the registry as empty. Normal
retention never removes an active barrier.

Repository admission is bounded authority-wide and per principal by live count/bytes,
enrollment rate/concurrency, and retired-binding index count/bytes. Before sequence
advancement, enrollment reserves its live/audit capacity and one eventual fixed-size
retired-binding entry; exhaustion returns `RepositoryCapacityExceeded` without
stranding existing status, recovery, transfer, or retirement. Retirement folds bulky
detail into a fixed-size cumulative authenticated `RetiredRepositoryAccumulator` and consumes the
reserved protected index entry containing domain-separated keyed repository/binding digests, final
generations/watermarks, and audit root. The continuity-protected index has no deletion
or false-negative path; ambiguity rejects enrollment, and a full index blocks new
enrollment rather than allowing unbounded growth or identity reuse.

Every apply plan records a
`CoordinationDomainId` and proves either `ExclusivePrincipal` ownership (no other
non-administrative principal or host in the threat model can write/replace the
checkout, Git common directory, and their
replace-capable ancestors; its payloads may remain private while admission and active
barriers remain in the registry) or a `SharedAuthority` outside repository control
used by every authorized principal and adapter. The shared authority owns the intake
fence, transaction reservation, active state, `RecoveryRequired` barrier, fencing
epochs, bounded outcome retention, the retirement watermark/accumulator, and pinned
records. Without either proof the repository is inspect-only and apply
returns `RejectedUnsafeCoordination` with no
repository effect. Ownership/authority changes are revalidated at every boundary;
transfer is explicit, preserves repository identity, and enters an authority-wide
`TransferPending` fence that blocks new epochs, preparation, dispatch-plan leases,
intake, and replay. It
waits for or reconciles every active operation under the existing reservation, resolves
or acknowledges recoverable barriers, proves every effect fence closed/drained (or its
worker terminated with write capabilities closed), treats `OwnerQuiescencePending` as
non-acknowledgeable, and proves dispatch-lease/intake/reservation/nonterminal state quiescent,
carries retained/pinned state, then atomically increments `CoordinationGeneration`,
installs the new owner/writer set, and rotates credentials. A crash leaves old
ownership, completed new ownership, or the durable fence, never two writable domains.

The immutable prepared plan contains all path and project operations, expected revision
and hashes, coordination-domain/ownership proof, encoding/newline/file-mode policy,
symlink/hard-link/root-containment policy, exact diff, diagnostics, assumptions,
verification, expiry, and recovery data. `PlanHash` is SHA-256 over one versioned
host-neutral canonical plan payload with the `PlanHash` field omitted entirely; shared
vectors keep every adapter byte-identical, and verification reserializes that payload
before comparing the digest. Source/destination paths are repository-contained;
staging, backups, and control artifacts use a protected operation root outside and
non-overlapping with the repository, on the target volume when required. Before
prepare returns, its canonical envelope is atomically persisted outside repository
control in a user-private store for `ExclusivePrincipal` or authority-controlled
storage for `SharedAuthority`. Preview and apply consume that same repository-scoped,
hash-verified plan across process restart.

Under [ADR-0013](adr/0013-fenced-plan-unavailability-resolution.md), a quota-bounded
`DispatchPlanLease` validates and pins the exact plan under the caller-known operation
and canonical request identity before any request bytes are sent. `Received` atomically
adopts that pin. Garbage collection retains it through authoritative dispatch
resolution and every nonterminal state, including `Recovering`, through
`OwnerQuiescencePending` and `RecoveryRequired`, and while a `CancelledNoEffect`
control bundle awaits verified
cleanup. Any terminal no-effect operation, including `Expired`, keeps the adopted plan,
journal, artifact inventory, quota, and cleanup capability pinned while
`CleanupPending`; `RejectedPlanUnavailable` keeps its immutable identity/hash, any
remaining verified bytes, failure evidence, and the rest of that cleanup bundle without
reconstruction. Durable lease/replay expiry or epoch sealing releases only an
unadopted pin for which no operation/control artifact exists; an adopted pin releases
only after verified terminal cleanup or explicit recovery acknowledgement.

Apply creates a durable operation record, initializes its journal before staging, stages only in the outside-repository control root, acquires an exclusive canonical-repository transaction reservation, claims a renewable owner lease and monotonically increasing fencing epoch, validates every precondition, arbitrates cancellation against a complete journalled first-effect permit in one durable compare-and-swap, journals changes, revalidates the expected intermediate repository state at every side-effect boundary, applies deterministically, verifies postconditions, and records one truthful state. A first-effect permit is eligibility to request one effect, not reusable write authority: a non-bypassable OS-enforced broker/fence revalidates authority at the actual conditional filesystem boundary and serializes effect admission with fence closure. Interrupted pre-effect staging cleanup is idempotent and never touches the repository.

Pre-effect staging/backup/control-root writes, guard acquisition/release, and final
preflight also use a non-bypassable `PreEffectActivityFence` bound to an owner control
generation. Cancellation or another no-effect resolution closes new effect and
pre-effect admission and advances that generation atomically. Admitted activity must
drain, all guards must release, and an exclusive rescan must prove the operation root
empty before cleanup can be marked verified; otherwise the terminal no-effect result
remains `CleanupPending` with its plan, journal, artifacts, quota, and cleanup capability
pinned.

- `Received` or authoritative `NotRecorded` during replayable dispatch resolution;
- `Applying`, `Recovering`, `OwnerQuiescencePending`, or bounded `Reconciling` while nonterminal;
- `Applied`;
- `RolledBack`;
- `RecoveryRequired` as a durable intervention state;
- `RecoveredApplied` or `RecoveredRolledBack` after verified manual recovery;
- `RecoveryAcknowledged` as an administrative terminal state that does not claim cleanliness;
- `CancelledNoEffect` as a terminal accepted cancellation with proof that no repository effect began;
- `RejectedPlanUnavailable` as a terminal no-effect fence when the leased exact plan cannot be verified before the first effect;
- `RejectedUnsafeCoordination` when cross-principal/cross-host mutation coordination cannot be proven;
- stale/conflict rejection; or
- expiry.

The ledger keeps an append-only transition history as well as the current state. An incomplete rollback is `RecoveryRequired`; it is never presented as clean failure or rollback. That state is committed to the coordination authority and cross-principal/cross-host registry before reservation release and blocks a different apply from any principal or host until fenced recovery verifies `RecoveredApplied` or `RecoveredRolledBack`, or an authorized user explicitly records `RecoveryAcknowledged`. Acknowledgement performs no repository effect and records actor, reason, observed hashes, unresolved warnings, and the absence of a cleanliness guarantee.

An expired `Applying` owner is never left or reported as healthy live work. Startup and
status lookup first close and drain the effect fence or prove worker termination and
write-capability closure. Proven quiescence permits one reconciler to advance the epoch,
transition through `Reconciling`, and use the durable plan, journal, backups, and observed hashes to
prove `Applied`, prove `RolledBack`, or require explicit recovery. If the fenced
reconciler instead proves the intact journal has no `FirstEffectStarted` permit and no
repository effect was possible, it reruns final preflight and may record the applicable
`CancelledNoEffect`, `RejectedStale`, `RejectedUnsafeCoordination`, or `Expired`
outcome without reverting external drift; ambiguous evidence is `RecoveryRequired`.
Until quiescence is proven, the operation remains `OwnerQuiescencePending` behind the
authority-wide intervention barrier rather than falsely advancing ownership.
An expired
`Recovering` owner is reconciled under the same reservation, lease, and fencing
rules and ends as `RecoveredApplied`, `RecoveredRolledBack`, or
`RecoveryRequired`.

The reservation is acquired before final whole-plan preflight and held through the
durable terminal result, verified rollback, or explicit recovery handoff. Because it
does not exclude editors/builds, apply also acquires a deterministic OS-enforced
guard set over every reviewed dependency, affected object, and directory entry, or
uses equivalent atomic conditional effects. Both boundaries remain held through
final verification and durable outcome; advisory locks and unconditional atomic
replacement are insufficient, and unsupported platforms block unattended apply.
No second
apply or recovery transaction for the repository can enter final validation while
it is held. At every journalled effect, the owner verifies the reservation, ledger
state, fencing epoch, lease, prior effect outcomes, and every still-relevant plan
dependency. A reconciler acquires the same reservation before advancing the epoch;
it also closes and drains the effect fence or proves worker termination and write-handle
closure before advancing. If neither can be proven, the epoch remains current and the
operation enters nonterminal `OwnerQuiescencePending` behind an authority-wide barrier;
it cannot transition to or acknowledge `RecoveryRequired`, transfer, retire, or admit
queued work while the stale holder may execute. After safe takeover, a resumed stale
owner cannot pass broker admission or perform another write. Drift before the first effect returns stale rejection; drift
after any journalled effect stops the remaining plan and verifies rollback to
`RolledBack` or `RecoveryRequired`, never stale rejection or applied success.
Recovery artifacts stay pinned through `RecoveryRequired`
and `Recovering`; verified recovery releases them only after cleanup, while explicit
acknowledgement follows the recorded retention choice and preserves an immutable
audit summary.

After durable `Received`, cancellation is recorded against the composite operation
identity. If receipt-to-ledger promotion is pending, the same cross-process intake
fence atomically lets either promotion or cancellation win, so cancelled work cannot
be enqueued afterward. Before the first repository effect, cancellation competes
with the owner's effect permit in one store-enforced compare-and-swap. It wins as
`CancelledNoEffect` only when `FirstEffectStarted` is absent; the record includes its
request hash, timing, journal sequence, staged-artifact inventory, no-effect proof,
and cleanup status. The winning transaction also closes pre-effect activity admission
and advances its control generation, so a stale worker cannot create another artifact
or acquire a guard after cleanup. If the effect permit wins, later cancellation is recorded but
verification and rollback remain non-interruptible. Replay returns the existing
cancellation result while its outcome record is retained and
`OperationEpochRetired` after compaction; neither restarts work. Startup/status lookup
performs idempotent control-artifact cleanup outside the repository only after the
pre-effect fence drains and guards release, while retaining the plan until cleanup is
verified.

Before dispatch the caller obtains a bounded, authenticated repository operation-epoch
token and constructs `OperationId` from its monotonic epoch plus a caller-generated
nonce. The operation epoch is distinct from owner fencing epochs. Epoch tokens are
repository/principal-bound, schema-versioned, count/byte/time-limited, and carry
`NotAfter`; the separate one-use `NotRecorded` replay epoch never extends token
validity. The caller also generates the canonical request hash before dispatch.
The hash is SHA-256 over one versioned host-neutral serialization of all semantic
apply fields, including the structured operation ID and
`OperationEpochClaimsHash`; that claims hash covers the token's canonical public
scope, epoch, limits, and `NotAfter`, but not its signature. The request hash omits
`RequestHash` itself, replay and transport attempt metadata, cancellation state, and
authentication proof. Every adapter uses the same canonicalizer and vectors instead
of hashing its wire representation.
Before sending request bytes, the caller registers a quota-bounded
`DispatchPlanLease` that validates the epoch/principal, canonical request identity,
and exact plan envelope/hash, reserves dispatch-resolution outcome capacity, and pins
those bytes through receipt adoption, authoritative status resolution, or epoch
sealing. Failure occurs before dispatch;
the lease alone does not mean the request was accepted.
Canonical repository ID plus operation ID is the composite key for every receipt,
ledger, status, recovery, acknowledgement, and retention lookup; the same operation
ID in another repository is independent.
Before accepted work enters an execution queue, a cross-process, store-enforced
intake fence keyed by repository and operation ID protects a durable `Received`
receipt containing the canonical request. Intake validates the token, claims hash,
principal binding, matching dispatch lease, exact plan availability, and reserved
outcome capacity first; token/quota validation, plan-pin adoption, and receipt
creation are atomic with epoch sealing. Startup and status
lookup reconcile any receipt not yet promoted into the operation ledger. While a live
or retained record exists, repeating the same operation ID and request returns the
existing receipt/state/result and reusing it for different work returns conflict.

Terminal outcomes remain individually addressable for a bounded replay window. The
authority caps dispatch leases/pinned plan bytes, open operations, simultaneously
open/unretired epochs, epoch issuance, accepted count/bytes, retained outcome bytes,
and pinned recovery/cleanup exceptions;
exhaustion rejects new mutation admission without blocking status or recovery. A
sealed epoch rejects an otherwise unknown operation as `OperationEpochSealed` and
cannot create `Received` or `NotRecorded`; sealing atomically converts outstanding
`NotRecorded` records to terminal `Expired` and invalidates their replay authorization.
Admission reserves the bounded ledger,
outcome, and recovery-metadata capacity an accepted operation may need, so quotas
cannot block truthful terminal/recovery state. After sealing an epoch, the authority
folds eligible outcome
digests into a cumulative accumulator, advances a monotonic `RetiredThroughEpoch`
watermark, and only then deletes individual records. Pinned/live records resolve
first. Every otherwise unknown ID at or below the watermark returns
`OperationEpochRetired`, never `NotRecorded` or executable work. The watermark and
accumulator are constant-size and never move backward. Identity retirement is a
separate `RetirementPending` fence that blocks new mutation, transfer, and dispatch
leases but preserves status/recovery while all epochs seal, leases and active work
quiesce, every effect fence closes/drains or its worker termination is verified,
`OwnerQuiescencePending` clears, and recovery and cleanup
finish (or recovery is explicitly acknowledged), pinned records release, and retained outcomes compact
through the final issued epoch. Only then does one transaction replace the live
mapping with the enrollment-reserved fixed-size retired-binding entry and fold the full
marker into the retired-repository accumulator. A pinned operation therefore
can never lose its only status or recovery key.

### 7. Transport contract

Transport outcome describes only what the caller can prove:

- **NotDispatched** — no request bytes were accepted; policy may permit fallback or retry.
- **Completed** — a valid operation response was received.
- **Indeterminate** — dispatch may have occurred but no valid response is available.

An indeterminate mutation is never automatically retried or executed in-process. The
host already knows the repository/operation identity and queries status. If no receipt
or operation exists, status lookup validates the original token and canonical request
identity, inspects the matching dispatch lease, then uses the intake fence to exclude a
still-arriving request. If the exact plan remains readable and verified, it atomically
records `NotRecorded` with a replay epoch while the operation epoch is open; only then
may the caller explicitly resubmit the identical request under the same identity. If
the lease or plan is unavailable or invalid, it instead records terminal no-effect
`RejectedPlanUnavailable` with no replay authorization. Both outcomes fence a delayed
original, and the rejection must be observed before the caller re-prepares and uses a
new current-epoch operation ID. A sealed or retired epoch never authorizes replay.

Cancellation is `NotDispatched` only when no request bytes were accepted. Once any
bytes are accepted, cancelling or timing out the response wait is `Indeterminate`,
even before `Received` is durable; it is not an ordinary cancelled operation result.
The caller retains the same ID and status resolution races receipt creation under the
same intake fence. A winning `Received` is cancelled idempotently through the canonical
operation; a winning `NotRecorded` fences the receiver, and an in-flight receiver keeps
status indeterminate. No new apply or operation ID is created automatically.

For the current hardening phase, the daemon is an optional transparent typed transport adapter under [ADR-0008](adr/0008-daemon-transparent-passthrough-phase.md), with fail-closed endpoint authentication under [ADR-0009](adr/0009-authenticated-daemon-endpoints.md). It has no workspace cache, operation authority, CLI argument bridge for mutations, process-global request context, or raw payload logging. Every connection authenticates the client principal and daemon instance through OS-enforced peer identity/access control or an equivalent protected mutually authenticated local channel. If that boundary cannot be proven, the daemon remains disabled and direct execution may be selected only before dispatch. It must preserve the same typed observable result as direct execution.

## Output pipeline

```text
Application operation returns typed result/error envelope
                         │
                         ├─ CLI renderer -> text / JSON / YAML / Markdown / LLM view
                         ├─ MCP adapter  -> structuredContent + equivalent text view
                         └─ future IDE   -> native presentation
```

Rendering is downstream of application semantics. Output shapes are typed records, not command-local dictionaries. Strict JSON escaping remains the default unless a caller explicitly selects the relaxed compatibility view.

Human text is not the MCP machine contract. A structured MCP tool returns `structuredContent` that validates against its output schema, with text as an additional view.

Suggestions/follow-ons are application metadata projected by hosts. They are not invented by a renderer, and destructive follow-ons are suppressed when workspace completeness, confidence, or safety policy does not allow them.

## Capability catalogue as the source of truth

| Concern | Canonical source |
|---|---|
| Command/tool identity, aliases, category, visibility, maturity | Application definition |
| Command name, category, examples, mutability, and JSON-output flag | `RtCommandInfo` within the definition; CLI projection must match |
| Request/result types, validation, and alternative request variants | Typed operation contract and host-neutral property metadata |
| Safety, execution placement, limits, paging, trust requirements | Application definition/policy |
| Reusable option names, defaults, aliases | Capability catalogue |
| Capability interface to catalogue flag binding | Shared binders |
| Help, examples, known limitations, follow-ons | Same application metadata |
| MCP input/output schema | Generated from typed contracts, property metadata, and alternative request variants |
| `prime`/agent primer | Generated from stable catalogue entries |
| Generated command documentation | Catalogue projection, verified rather than rewritten by normal build |

If two surfaces describe the same fact, the catalogue wins and CI rejects the drift.

## MCP boundary

The preferred MCP implementation is the official C# SDK. A handwritten protocol implementation is acceptable only with equivalent spawned-process conformance evidence for every advertised protocol version.

Stable MCP behaviour includes:

- correct request, response, notification, and parse-error handling;
- explicit supported-version negotiation or selection;
- cancellation and progress for long work;
- complete JSON Schema, including enums, bounds, nullability, nesting, and additional-property policy;
- schema-valid structured results;
- bounded request/output/item/time/concurrency/memory behaviour;
- revision-bound deterministic pagination, or explicit non-resumable projection/truncation; and
- no stable apply-capable CLI bridge.

Protocol version and server version are release facts derived from one source, not hard-coded aspirational values.

## Safety and quality gates

- **Architecture tests** enforce category, definition/port, output, dependency, parser, and host-boundary invariants.
- **Mutation safety tests** inject failures at every stage/write/replace/move/delete/verify/journal/rollback boundary.
- **Workspace tests** cover complete, partial, failed, trusted, isolated, and untrusted cases.
- **Protocol tests** spawn the packaged MCP process and exercise malformed input, notifications, version negotiation, cancellation, large results, schema validation, disconnects, and continued availability.
- **Parity tests** compare typed observable outcomes across direct, CLI, MCP, and transparent-daemon paths.
- **Filesystem tests** run on Windows, Linux, and macOS for encoding/BOM/newlines, permissions, locks, symlinks, hard links, case-only changes, and interrupted recovery.
- **Dependency/supply-chain gates** include locked release restore, vulnerability/dependency review, static security analysis, and controlled action dependencies.
- **Command admission** blocks stable commands without executable examples, validation, typed expected failures, limits, provenance/confidence, cancellation, and proportional fault-injection evidence.

Coverage and mutation-testing claims are executable CI configuration, not only documentation. Skipped tests for known defects remain linked to tracked work and do not count as safety evidence.

## Hermetic build and release

A normal `dotnet restore`, `dotnet build`, or `dotnet test` does not:

- format or rewrite source;
- regenerate tracked documentation;
- deploy skills or binaries into the user profile;
- start or depend on a daemon; or
- modify anything outside declared build/temp outputs.

Formatting, document generation, skill deployment, dogfood analysis, mutation testing, and installation are explicit commands or opt-in targets.

Assembly, NuGet, MCP manifest, archive, and release versions derive from one source. Documentation advertises only artifacts actually published and smoke-tested. Native release jobs install, launch, exercise, upgrade, roll back, and uninstall each supported RID while preserving files they do not own.

## Dogfood loop

```text
Agent or developer runs rt on a real task
  -> typed operation succeeds or returns an evidence-rich limitation
     -> limitation/bug is captured with request context and redacted diagnostics
        -> triage links it to one authoritative issue/spec
           -> TDD: failing regression -> implementation -> refactor
              -> parity + fault injection + coverage/mutation gates
                 -> generated metadata/examples verified
                    -> release/admission evidence recorded
                       -> next user benefits
```

The tool is its own customer, but dogfooding does not replace correctness evidence. A tool that analyses itself can share its own blind spots. External fixtures, adversarial projects, cross-platform filesystems, protocol clients, and fault injection remain mandatory.

## Current migration tensions

- **The canonical operation migration is incomplete.** A subset of commands has typed ports; the remainder still uses compatibility paths. The migration manifest is the current source for counts. Complete the five-command spine before broad fan-out.
- **Legacy daemon routing remains a safety boundary.** Typed recovery improved migrated commands, but legacy argument-array fallback is not acceptable for mutations and must be removed first.
- **The staged prepared writer is still process-local.** It now preflights and stages supported writes/moves/deletes, backs up originals, suppresses cancellation during commit, and attempts compensating rollback. It does not yet provide a durable plan store, complete path/metadata coverage, a durable journal and status ledger, handle-safe symlink/reparse containment, restart reconciliation, or recovery commands.
- **Workspace loading does not yet expose completeness or trust.** Existing warning suppression and partial-load tolerance cannot support proof-strength analyses until the new contract lands.
- **MCP is experimental until conformance gates pass.** The current handwritten server and text-only result model are transitional.
- **Host projects remain physically coupled.** `RoslynTools.Mcp` still references the CLI project while descriptor/schema and compatibility concerns live there. Full port coverage allows both hosts to depend on the application/core assemblies without an executable-project reference.
- **Capability flags remain partly ad hoc.** Continue the central-catalogue migration; do not create more command-local known options.
- **Output and suggestion fan-out remain incomplete.** Follow-ons, schemas, provenance, confidence, and limitations must be uniform before stable admission.
- **Build/release claims exceed current evidence.** Debug builds currently have side-effecting targets, version metadata claims `1.0.0`, and distribution verification is not yet native end-to-end. Treat release hardening as product correctness.
- **A real workspace daemon is parked.** Reconsider it only after trust, mutation, protocol, and canonical-operation gates pass and measured cold-start cost justifies the added state model.

## Updating this document

When a decision changes a layer contract, trust boundary, mutation state, output doctrine, enforced invariant, compatibility promise, or accepted non-obvious trade-off:

1. Write or supersede an ADR under [adr/](adr/). Accepted ADR history is not silently rewritten beyond status/link maintenance.
2. Update this overview to reflect the resulting architecture, including current migration tension where implementation lags.
3. Update the most specific companion document and OpenSpec requirement/task where applicable.
4. Add or update executable architecture/acceptance tests for the decision.
5. Commit the decision, overview, specific document, and tests as one coherent change where practical.

Architecture documents distinguish **accepted target**, **implemented evidence**, and **remaining migration**. A target statement is never presented as a runtime guarantee until its gate passes.
