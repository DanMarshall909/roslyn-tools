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

- request ID and, for apply, a caller-known structured operation ID from a bounded authority-issued epoch;
- repository root and solution path;
- working directory only as an explicit absolute path;
- configuration, target framework selection, SDK identity, and environment allow-list;
- trust state and evaluator mode;
- cancellation and resource budget; and
- client/host identity for diagnostics, not authorisation.

Operations do not read or mutate process current directory, global console state, `Environment.ExitCode`, or arbitrary process environment as request context.

## Trust and evaluator-isolation boundary

`WorkspaceRequest` declares two independent axes:

- `TrustState` is `Trusted` only under the authenticated user's explicit decision recorded in a protected principal-scoped registry for the canonical repository identity and verified physical binding; otherwise it is `Untrusted`.
- `EvaluatorMode` is `NoEvaluation`, `InProcess`, or `IsolatedWorker`.

`Untrusted` can be paired with `NoEvaluation` or `IsolatedWorker`, never
`InProcess`. `Trusted` can still be paired with `IsolatedWorker` when the user or
policy requests defence in depth. Isolation is therefore an execution decision, not
a trust state.

The request-carried trust value is an expected-state or downgrade constraint, never
an authority claim. Before evaluator admission, the application operation derives the
effective value from the protected registry and treats an absent, revoked, unreadable,
mismatched, or stale-binding decision as `Untrusted`. Only authenticated
`GrantRepositoryTrust` and `RevokeRepositoryTrust` operations reached through a
trusted surface with deliberate user action can change the record; ordinary CLI, MCP,
or daemon requests, repository content, and evaluator output cannot. The record is
durably audited, and non-interactive callers cannot bootstrap trust.

Grant, revoke, and `InProcess` evaluator admission use one linearizable trust gate per
authenticated principal, canonical repository identity, and verified physical binding.
Admission atomically re-reads the grant and acquires a revocable evaluator lease bound
to the current monotonic `TrustGeneration` plus a held verified-root capability before
repository-controlled code can run. That capability pins the grant's physical root and
provides the evaluator a handle-relative or immutable snapshot view. The actual
solution/build open consumes and revalidates the capability rather than re-resolving
the canonical ambient path; if an evaluator cannot preserve that binding through
startup, `InProcess` is refused in favour of isolation or a typed safety failure. An
earlier trust read is not an admission token. Revoke atomically closes admission,
increments that generation, persists `Untrusted`, and records the audit event, so a
request paused before lease acquisition cannot start afterward. Evaluations whose
leases linearized first are already admitted: the authority cancels or terminates them
and their descendants through an enforceable supervisor and reports successful
revocation only after verified quiescence. While any old-generation lease is not
proven quiescent, revocation is durably `RevocationPending`, the repository remains
untrusted, and both new in-process admission and a replacement grant stay fenced.
Missing or unreachable lease state fails closed and is resolved through status and
reconciliation rather than being assumed quiescent.

`InProcess` does not mean uncontained descendants. Before admission, an external
supervisor that executes no repository code places the evaluator host and every process
it can create inside a non-breakaway OS job/cgroup/process domain and issues a
generation-bound containment lease. Evaluated code cannot detach, re-parent, disable,
or escape the domain; the supervisor retains an out-of-domain kill capability and
durable status. Revocation requests cooperative cancellation, then terminates the
per-admission domain or the entire containing host when descendant attribution cannot
be proven, and remains pending until the OS reports the revoked domain empty. Whole-host
termination truthfully interrupts other requests. A platform without non-escape,
external kill, and empty-domain proof refuses `InProcess`.

The isolated worker runs as a distinct least-privilege security identity inside
OS-enforced process/PID, process-handle, IPC, filesystem, and network namespaces or
equivalent sandbox boundaries. It cannot authenticate as or impersonate the host user,
enumerate/open/debug/signal/inject into host processes, or reach undeclared pipes,
sockets, shared memory, or other same-machine IPC. It inherits no ambient handles;
only bounded schema-validated operation-scoped supervisor channels are present, and
every child remains in the same containment and resource limits.

The namespace exposes only the repository through a read-only mount or equivalent
handle boundary, explicitly declared read-only SDK/package/tool inputs, and separate
disposable writable build-output, intermediate, analyzer/package-cache, and
temporary locations outside the checkout. Host filesystem roots, user profiles,
SSH keys, credential stores, undeclared package caches, device endpoints, and other
ambient paths are not mounted or visible. The worker inherits no secrets, has
bounded CPU/memory/time, and has no network or externally routed socket namespace
while any untrusted repository-controlled code can execute. Network access is never
enabled inside that phase. Required SDK/package/tool acquisition occurs beforehand in
a separate trusted broker that executes no repository code, accepts only
policy-allow-listed registries and user-reviewed lockfile-pinned coordinates, verifies
content hashes/signatures, and materializes the results as declared read-only worker
inputs. Missing inputs stop evaluation with a typed diagnostic; they do not trigger
restore from inside the worker. If the platform cannot prove identity separation,
process/handle/IPC isolation, filesystem containment, and network egress denial,
`IsolatedWorker` evaluation is refused.

The checkout view is not a blind bind mount. The supervisor opens declared inputs
relative to the verified root with no-follow/reparse-safe primitives and validates
final-handle identity, type, and link metadata before content is consumed. Multiply
linked files are rejected unless every alias is proven within declared roots. Accepted
content is materialized into an immutable per-operation filesystem with private inodes
that cannot alias host objects, unless the platform proves equivalent alias containment
for the mounted snapshot lifetime. Rejected external-alias bytes never enter worker or
host-visible output, diagnostics, logs, or caches.

Admission reserves both per-operation and aggregate host capacity. OS-enforced limits
inherited by every descendant cap live and total process/task creation, threads,
process-table entries, open handles/file descriptors, CPU, memory, wall-clock time,
and IPC/result bytes. Separate disposable filesystems enforce byte and inode/file-count
quotas for outputs, intermediates, caches, and temporary data. Exceeding a limit stops
evaluation with a typed resource outcome. Every terminal path and restart
reconciliation terminates descendants, revokes channels, and destroys or quarantines
writable namespaces within a bounded deadline; capacity is not reusable until cleanup
is verified.
Repository-controlled tasks, analyzers, or generators that attempt repository
writes or undeclared host reads are denied and audited; evaluator completion never
copies undeclared writes back into the checkout. If the platform cannot establish
this namespace, `IsolatedWorker` evaluation is refused.

`NoEvaluation` is not an ambient-filesystem permission. For untrusted repositories,
all host-side file-only reads go through one safe reader rooted at a verified checkout
directory handle. It opens each component with no-follow/reparse-safe primitives,
checks the final handle's filesystem identity before reading, and permits only
operation-declared regular files/directories. It rejects symlinks, junctions, mounts,
reparse points, special files, alternate streams, and hard links unless every alias
is proven inside the declared roots. Repository text cannot declare a host path into
scope. If the platform or operation cannot prove that boundary, the read is routed to
the allow-listed worker or refused; denied target contents never enter diagnostics,
results, logs, or caches.

The CLI, MCP host, and daemon are long-lived privileged processes and SHALL NOT evaluate an untrusted repository in-process. A trust decision is scoped to the authenticated principal, canonical repository identity, and verified physical binding; it can be inspected and revoked through the protected trust authority. It is never inferred merely because a checkout is local or an editor opened it.

Workspace provenance records:

- canonical repository and solution identity;
- verified physical-root capability and immutable snapshot identity consumed at
  evaluator startup;
- in-process descendant-domain identity, external supervisor, and containment
  generation where applicable;
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
`PlanHash`. `PlanHash` is SHA-256 over a versioned host-neutral
`CanonicalPlanPayload` serialization in which the `PlanHash` field is omitted
entirely; it is never hashed as an empty, zero, or provisional value. Every host uses
the same path/string normalization, property ordering, explicit-null, default-value,
and byte-encoding rules plus shared conformance vectors. The persisted plan envelope
contains that payload and its claimed hash; verification reserializes the payload
without the hash field and compares the digest in constant time. Before prepare
succeeds, the exact envelope is atomically persisted in a protected durable plan store outside the target repository:
user-private
under proven `ExclusivePrincipal`, or authority-owned/access-controlled under
`SharedAuthority`. The store is logically scoped by canonical repository identity and
survives CLI, MCP, and daemon process restart. `Apply` receives both the plan identity and a
caller-generated `OperationId` before any transport dispatch:

```text
EnrollRepository(enrollmentRequest) -> RepositoryIdentity
Prepare(request) -> PreparedChangeSet
GetOperationEpoch(repositoryId) -> OperationEpochToken
Apply(repositoryId, planId, planHash, operationEpochToken, operationId, requestHash, expectedRevision, replayEpoch?) -> ApplyResult
GetOperation(repositoryId, operationId, requestIdentity?, operationEpochToken?) -> OperationStatus
CancelOperation(repositoryId, operationId) -> OperationStatus
Recover(repositoryId, operationId, recoveryAction) -> RecoveryResult
TransferRepositoryOwnership(repositoryId, transferRequest) -> RepositoryIdentity
RetireRepositoryIdentity(repositoryId, retirementRequest) -> RepositoryIdentityStatus
```

### Mutation coordination domain

Canonical repository identity is stable across OS principals and hosts: an OS- or
service-protected registry outside repository control binds the checkout filesystem
identity, Git
common-directory identity, and a non-reused coordination nonce. The registry cannot
be shadowed by a per-user store and records the coordination mode, declared owner or
authority, active recovery-barrier summary, and identity-retirement marker. Every
would-be mutation principal and host must resolve it through the same enforced
registry, so a second user or machine opening the checkout cannot derive an
independent repository ID and bypass existing operation state. A machine-local
registry qualifies only when filesystem/ownership proof excludes writes from other
hosts; shared or network storage requires a shared authority spanning every writer.
Normal retention never deletes the mapping or an active barrier.

First-time enrollment is an explicit, separately authorized registry operation; the
prepare/apply path never performs it implicitly. The registry maintains an
OS/service-protected `AuthorityId`, continuity generation, monotonically increasing
`RepositorySequence`, and append-only enrollment/retirement audit root. Startup and
restore must prove that generation, sequence high-water mark, and audit root continue
from the last protected checkpoint; an absent, rolled-back, unreachable, or
inconsistent registry cannot treat itself as empty.

Authority provisioning is a separate administrator operation in a demonstrably fresh
OS/service namespace backed by a non-rollback continuity anchor; it is not repository
enrollment. If the platform cannot prove fresh provisioning or later continuity, it
does not qualify as a mutation coordination authority.

`EnrollRepository` opens verified handles for the checkout root and Git common
directory without evaluating repository code, derives their stable physical identity,
authenticates an actor allowed to establish the declared coordination mode/writer set,
and acquires one authority-enforced enrollment fence for that physical identity. Under
one transaction it checks live and retired bindings, proves authoritative absence in
the continuity-complete registry, advances `RepositorySequence`, and persists the new
`RepositoryId = (AuthorityId, RepositorySequence)`, non-reused coordination nonce,
owner/authority proof, initial no-barrier state, physical binding, and audit entry
before returning. Concurrent enrollment returns the committed identity. A crash may
leave a sequence gap but can never reuse a value or expose a partially initialized
mapping.

A healthy registry with no binding returns typed `RepositoryNotEnrolled` to mutation
and offers the explicit enrollment operation. A live binding is reused, not replaced;
a retired or conflicting binding is never revived automatically. Missing authority
state, failed continuity proof, a repository-supplied identity marker, handle/path
replacement, or inconsistent binding returns `RejectedUnsafeCoordination` and cannot
mint an identity. A replacement checkout must present a distinct proven physical
identity and receives a later sequence; ownership changes use the explicit transfer
path.

Every apply-capable plan declares one `CoordinationDomainId` and mode:

- `ExclusivePrincipal` requires an OS ACL/mode/ownership proof that no other
  non-administrative principal or other host in the declared threat model can write or
  replace the
  repository root, Git common directory, or ancestor directory entries that can
  replace either. Trusted OS administrators act only through the explicit transfer
  path. In this mode the
  principal's protected user-private store may hold the full ledger and payloads, but
  admission, ownership, active recovery-barrier status, and transfer remain
  authoritative in the cross-principal registry.
- `SharedAuthority` requires one OS- or service-enforced authority outside repository
  control that every authorized mutation principal and host adapter must use. It owns
  the intake fence, active operation state, transaction reservation, fencing epoch,
  recovery barrier, bounded operation-epoch state, and retirement watermark. Operation
  payloads and recovery artifacts remain access-controlled and may be encrypted or
  referenced without exposing them to every principal.

Mutation preparation records the writer-set and authority proof. If another
principal can write the checkout and no shared authority is available, preparation
does not return an apply-capable plan and apply records no-effect terminal
`RejectedUnsafeCoordination`. Inspection remains available. Apply revalidates the
proof before the first effect and at every effect boundary. Pre-effect ownership or
authority drift also returns `RejectedUnsafeCoordination`; drift after an effect
enters verified rollback or `RecoveryRequired`.

`RecoveryRequired` is committed to the coordination authority and the
cross-principal registry before the transaction reservation is released. Every later
apply, from any OS principal, must resolve that registry and authority and observe the
barrier. Ownership transfer cannot silently rebind the
checkout to a new user-private ledger. It is an explicit administrative operation
under an authority-wide repository transfer fence and preserves `RepositoryId` for
the same physical checkout. The authority atomically enters `TransferPending`, blocks
new operation epochs, apply-capable preparation, dispatch-plan leases, intake, replay promotion, and
ownership changes, and seals outstanding operation epochs while leaving status,
cancellation, cleanup, and recovery available.

Transfer then waits for a live owner to finish or, after lease expiry, reconciles it
under the old repository reservation and fencing generation only after proving OS
reservation release plus a closed/drained effect fence or worker termination and
write-capability closure. Before committing new ownership, the authority proves there
is no dispatch plan lease or intake attempt/receipt promotion in flight, transaction
reservation, live owner, open/unreconciled effect fence, nonterminal operation, or
unresolved recovery barrier; `OwnerQuiescencePending` therefore blocks transfer.
`NotRecorded` replay authorizations are
expired, and every `RecoveryRequired` record is recovered or explicitly acknowledged.
It carries retained outcomes, watermark/accumulator, pinned terminal cleanup records,
plans, and audit history forward, rewraps access-controlled artifacts as needed, then
atomically increments `CoordinationGeneration`, installs the new owner/authority and
writer set, and rotates admission/fencing credentials before releasing
`TransferPending`. Old tokens, reviewed writer-set proofs, leases, and prepared plans
cannot cross that generation without revalidation. A crash exposes either the old
ownership, the completed new ownership, or a durable transfer-pending fence, never
two writable domains. Physical
checkout replacement uses quiescent retirement plus enrollment, not ownership
transfer. If the required
cross-principal proof or authority cannot be established, mutation is refused.

`(RepositoryId, OperationId)` is the composite durable identity for intake,
idempotency, status, recovery, acknowledgement, and retention. Before mutation
dispatch, the caller obtains a bounded `OperationEpochToken` from the coordination
authority and constructs `OperationId` from that authenticated monotonically
increasing epoch plus a caller-generated nonce. The token is repository-bound,
principal-bound, schema-versioned, count/byte/time-limited, and has an explicit
`NotAfter`; the operation epoch is distinct from owner fencing epochs. The token is
obtained before dispatch, so it is not lost with an apply response. Every API and
durable-store lookup that can observe or change an operation carries the structured
ID. `ReplayEpoch` is a separate one-use authorization for a proven `NotRecorded`
operation and never extends the operation epoch's validity. `RequestHash` is the
SHA-256 digest of a versioned UTF-8 canonical serialization of the semantic apply
request. That serialization includes repository ID, plan ID/hash, operation ID,
expected revision, `OperationEpochClaimsHash`, and every other work-defining field,
but omits `RequestHash` itself, `ReplayEpoch`, transport attempt/correlation metadata,
cancellation state, and authentication proof. `OperationEpochClaimsHash` covers the
token's canonical public repository/principal, epoch, schema, limit, and `NotAfter`
claims, but not its signature or bearer proof. Paths, strings, property order, nulls,
and defaults use one host-neutral canonicalizer shared by every adapter; adapters
never hash their transport JSON directly. A transport request-attempt ID may be
separate.

Before sending any mutation request bytes, the caller atomically registers a bounded
`DispatchPlanLease` in the protected coordination/plan authority under the same
repository and operation ID. Registration validates the epoch token and claims,
authenticated principal, canonical request identity, exact readable plan envelope and
hash, available pinned-byte/lease quota, and capacity for authoritative
`NotRecorded`/terminal dispatch resolution. The lease binds repository ID, operation
ID, plan ID/hash, request hash, expected revision, claims hash, and operation-epoch
expiry; it pins the exact plan until durable receipt adoption, authoritative dispatch
resolution, or epoch sealing. A lease does not mean transport accepted the request.
The receiver requires the matching lease, and `Received` atomically adopts its plan
pin. Normal retention therefore cannot expire a potentially dispatched plan while its
operation epoch can still admit or resolve that identity, and later quota exhaustion
cannot prevent its truthful dispatch-resolution record.
If transport proves `NotDispatched`, the caller may release the lease only through the
intake fence after the authority proves no intake attempt or receipt exists. A caller
crash before dispatch leaves only a quota-counted lease that expires when the epoch
seals; uncertainty uses status resolution rather than early release.

Before an accepted request can enter an execution queue, the receiver acquires a
cross-process, store-enforced intake fence keyed by repository and operation ID and
validates the operation-epoch token, its claims hash, and its binding to the current
authenticated principal. It also validates the matching `DispatchPlanLease` and exact
plan availability. The epoch-open check, quota debit/capacity reservation, lease-pin
adoption, and receipt creation are one authority transaction under that fence. It then durably
records `Received(OperationId, RequestHash, CanonicalRequest)`. The canonical request is stored in the protected coordination
control plane, not daemon logs. The operation ledger is then created before side effects
begin. Startup and status lookup reconcile a `Received` receipt that has no operation
record, so accepted work cannot disappear between transport intake and ledger
creation.

If indeterminate dispatch is followed by a status lookup and neither a receipt nor
an operation exists, `GetOperation` requires the original operation-epoch token plus
an `OperationRequestIdentity` containing request hash, plan ID/hash, expected
revision, and operation-epoch claims hash. It validates the token/request identity,
looks up the matching `DispatchPlanLease`, acquires the same cross-process intake fence, and first proves
that no receiver for that operation ID remains in flight. The epoch-open check and
record creation are atomic with sealing. If the exact plan remains readable and
verified, it durably records
`NotRecorded(OperationId, OperationRequestIdentity, ReplayEpoch)`. This is an
authoritative no-effect dispatch-resolution state, not an inference from a missing
row. Any delayed original attempt lacks that replay epoch and is rejected. While the
operation epoch remains open, the caller may explicitly resubmit the exact canonical
request with the same operation ID, request hash, and replay epoch; one
compare-and-swap transitions `NotRecorded` to `Received`. A lost
`NotRecorded` response can be queried again to recover the same epoch. This explicit
resolution is not an automatic retry or in-process fallback.

If the dispatch lease is missing/expired or its exact plan is missing, expired,
unreadable, schema-incompatible, or hash-mismatched, the same fenced resolution instead
records terminal no-effect
`RejectedPlanUnavailable(OperationId, OperationRequestIdentity, Reason, Evidence)`.
It returns no replay authorization, permanently rejects a delayed receiver for that
identity, and never revives if the plan later reappears. After observing this terminal
outcome, a caller may re-prepare against current inputs and use a new current-epoch
operation ID; it does not retry the unavailable plan.

While its live or retained record exists, reusing an `OperationId` with the same
canonical request returns the existing receipt, operation state, or result. Reusing
such an ID with a different plan, revision, request hash, or payload returns
`OperationConflict` and performs no work. `DispatchPlanLease`, `Received`, and
`NotRecorded` records are bounded: they pin the referenced plan until receipt adoption,
authoritative resolution, explicit replay, or durable expiry, and none can remain unresolved indefinitely.
Expiry records the terminal outcome for the active replay window; sealing the
operation epoch later makes every unknown or compacted ID in that epoch durably
unacceptable. An outstanding `NotRecorded` replay authorization expires when its
operation epoch seals and cannot be carried into a new epoch.

Apply loads the exact persisted plan bytes identified by repository ID, plan ID, and
plan hash. A missing, expired, unreadable, schema-incompatible, or hash-mismatched plan
is rejected before the operation ledger enters `Applying`. If no receipt exists,
status records the fenced `RejectedPlanUnavailable` dispatch resolution above; if
`Received` already exists, the operation atomically records the same terminal
no-effect outcome before `FirstEffectStarted`. Apply never reconstructs a
plan from command arguments or a newer workspace snapshot. The plan store uses
principal-private or authority-mediated access as declared by its coordination mode,
bounded retention for unreferenced plans and safely cleaned terminal history, atomic
writes, and crash-safe cleanup;
repositories cannot supply or replace its control metadata.

Bounded retention applies to unreferenced prepared plans, safely cleaned bulky
recovery data, detailed terminal transition history, and terminal outcome records
after their operation epoch is safely retired. Registering a `DispatchPlanLease` pins
the exact plan before dispatch; creating a receipt or operation atomically adopts that
pin by durable reference. Garbage collection cannot remove a plan while an associated
dispatch lease remains admissible or an operation is `Received`, `NotRecorded`, `Prepared`, `Applying`,
`OwnerQuiescencePending`, `Reconciling`, `Recovering`, or `RecoveryRequired`, and cannot remove a
`CancelledNoEffect` plan while its control-artifact cleanup remains pending. It
releases the plan only after verified terminal cleanup, durable expiry, or explicit
recovery acknowledgement. Recovery evidence and its plan are retained as one
referentially complete bundle.

### Bounded idempotency epochs

Every terminal transition atomically writes a compact outcome record keyed by
canonical `(RepositoryId, OperationId)` before reporting success or failure. During
the configured replay window, it retains the request/plan hashes, terminal state,
canonical outcome, completion time, final evidence digest, and required
recovery/cleanup summary. The intake fence checks live records and retained outcomes:
an identical request returns the existing outcome without work, and a different hash
returns `OperationConflict`.

The coordination authority enforces per-repository and per-principal limits for
dispatch-plan lease count/pinned bytes, open-operation count, simultaneously
open/unretired epochs, epoch issuance rate, accepted operations/bytes per epoch,
replay-window duration, terminal-record bytes,
and pinned recovery/cleanup records. An epoch token stops accepting unknown operation
IDs at its `NotAfter` or earlier count/byte limit. Quota
exhaustion returns typed `CoordinationCapacityExceeded` before dispatch or durable intake while
status, cancellation, cleanup, and recovery remain available. The authority does not
mint unlimited replacement epochs for a caller that exceeds its admission policy.
Admission atomically reserves enough bounded coordination-store capacity for the
receipt, transition ledger, maximum compact outcome, and required cleanup/recovery
metadata. A quota cannot prevent an accepted operation from recording truthful state
or completing recovery.

After an epoch is sealed, it accepts no new dispatch lease, `Received`, or `NotRecorded` record and
returns `OperationEpochSealed` for an otherwise unknown ID. In the same durable seal
transition, every outstanding `NotRecorded` record becomes terminal `Expired` and its
unused replay authorization is invalidated. Once intake attempts are quiescent, unused
dispatch leases expire and release their plan pins; already accepted operations continue.
Existing live/retained records remain queryable. Once the minimum replay window
elapses, the
authority atomically folds eligible terminal-record digests into a cumulative
`RetiredOperationAccumulator`, advances one monotonic `RetiredThroughEpoch`
watermark, and only then removes their per-operation records. Nonterminal,
`RecoveryRequired`, and cleanup-pending operations move to a bounded pinned-exception
index before the watermark advances; exact status/recovery still resolves those
records, and reaching the exception cap disables new mutation admission until they
are resolved or acknowledged.

Lookup checks pinned/live records first and then the watermark. Any otherwise unknown
operation whose epoch is at or below `RetiredThroughEpoch` returns durable
`OperationEpochRetired`, never `NotRecorded` or replay authorization. Thus a delayed
request cannot execute after compaction even though its individual outcome is no
longer retained. The accumulator and watermark are constant-size per repository and
normal retention cannot move them backward. Explicit repository-identity retirement
is distinct from ownership transfer and first enters a durable `RetirementPending`
fence that blocks new epochs, apply-capable preparation, dispatch-plan leases, intake, replay, and transfer
while preserving status, cancellation, cleanup, acknowledgement, and recovery access.
Under the existing repository identity/reservation, it seals all epochs and waits for
or reconciles every dispatch plan lease, intake attempt, receipt promotion, live owner,
reservation, effect fence, and nonterminal operation. `OwnerQuiescencePending` blocks
retirement until stale-writer quiescence is proven. Every `RecoveryRequired` record must reach verified recovery or
terminal `RecoveryAcknowledged`, every cleanup-pending record must verify cleanup, and
no plan/recovery/cleanup record may remain pinned. Every retained terminal replay
window must elapse and its record must be compacted so `RetiredThroughEpoch` covers
every issued epoch.

Only after those preconditions are durably proven does one transaction replace the
live mapping with a permanent retired-repository marker carrying the final watermark,
accumulator, physical binding, maximum repository/operation generation, retirement
actor/reason/time, and final audit root (including any acknowledgement summaries).
After commit there is no operation that can still require status or recovery, so every
later request carrying the retired repository ID is rejected before intake. If
quiescence or cleanup cannot be proven, retirement remains pending or fails without
removing access. Repository IDs and operation epoch numbers are never reused.

### Prepared plan contents

A prepared change set contains:

- plan ID, plan hash (excluded from its own canonical payload digest), schema version, creation time, expiry, repository identity, and expected repository revision;
- coordination domain ID/mode, stable cross-principal repository identity, declared mutation principal/writer set, and OS ownership or shared-authority proof;
- exact operation list for creates, replacements, deletes, moves, project edits, Roslyn documents, additional documents, and analyzer-config documents;
- expected existence and content hash for every read or modified path;
- expected file type, platform file identity, link count, known hard-link aliases, and symlink/hard-link policy;
- the OS/filesystem conditional-effect or exclusive-guard capability required for every read dependency, affected path, and destination directory entry;
- encoding, BOM, newline, and file-mode/permission preservation policy;
- root-containment proof that every source/destination is under the canonical repository root and every staging/backup/control artifact is under an operation-scoped coordination-domain-protected control root outside the repository;
- exact diff and machine-readable change model;
- diagnostics, assumptions, confidence, and irreversible limitations;
- verification steps and expected post-apply hashes; and
- cleanup and recovery policy.

Preparation performs no target-repository writes. Persisting the canonical plan in
the protected coordination control-plane store is part of preparation and must
complete before
the plan is returned. Review and dry-run rendering are projections of those exact
persisted bytes, and apply consumes those bytes without recomputation.

### Apply state machine

```text
Prepared
  -> RejectedPlanUnavailable  (only before FirstEffectStarted)
  -> Applying
       -> Applied
       -> RolledBack
       -> CancelledNoEffect  (only while FirstEffectStarted is absent)
       -> RejectedStale  (only while FirstEffectStarted is absent)
       -> RejectedUnsafeCoordination  (only while FirstEffectStarted is absent)
       -> Expired  (only while FirstEffectStarted is absent)
       -> OwnerQuiescencePending
            -> Reconciling
       -> Reconciling
            -> Applied
            -> RolledBack
            -> CancelledNoEffect  (only when NoEffectProven)
            -> RejectedStale  (only when NoEffectProven)
            -> RejectedPlanUnavailable  (only when NoEffectProven)
            -> RejectedUnsafeCoordination  (only when NoEffectProven)
            -> Expired  (only when NoEffectProven)
            -> RecoveryRequired
       -> RecoveryRequired
  -> CancelledNoEffect
  -> RejectedStale
  -> RejectedConflict
  -> RejectedUnsafeCoordination
  -> Expired

Unknown
  -> Received
       -> RejectedPlanUnavailable  (only before FirstEffectStarted)
       -> Prepared
       -> CancelledNoEffect
       -> RejectedStale
       -> RejectedConflict
       -> RejectedUnsafeCoordination
       -> Expired
  -> NotRecorded
       -> Received  (same request hash plus replay epoch only)
       -> Expired
  -> RejectedPlanUnavailable  (fenced dispatch resolution; no replay)

RecoveryRequired
  -> Recovering
       -> RecoveredApplied
       -> RecoveredRolledBack
       -> RecoveryRequired
       -> OwnerQuiescencePending
            -> Reconciling
       -> Reconciling
            -> RecoveredApplied
            -> RecoveredRolledBack
            -> RecoveryRequired
  -> RecoveryAcknowledged
```

`NoEffectProven` means the reconciler holds the fenced repository reservation and has
verified an intact ledger/journal with no `FirstEffectStarted` permit and no possible
repository effect. Outside-repository staging and its idempotent cleanup do not count
as repository effects. Under that proof, reconciliation reruns final preflight and may
record the applicable no-effect cancellation, stale, unsafe-coordination, or expiry
outcome without reverting external drift. If the proof is absent or contradictory,
the reconciler cannot use a no-effect terminal state and must resolve the possibly
effected journal or enter `RecoveryRequired`.

The ledger stores a current state for lookup plus an append-only transition history.
No recovery action rewrites or erases the original `RecoveryRequired` event. Direct
apply outcomes use `Applied` and `RolledBack`; verified manual-recovery outcomes use
`RecoveredApplied` and `RecoveredRolledBack`. `RecoveryAcknowledged` records an
explicit administrative handoff and does not assert that repository contents are
clean. `CancelledNoEffect` is a terminal result that proves the accepted request
performed no repository effect; it is unavailable after the durable
`FirstEffectStarted` boundary. `RejectedUnsafeCoordination` proves the required
exclusive-principal ownership or shared authority was unavailable before any
repository effect.

The implementation:

1. loads and verifies the exact persisted plan;
2. creates or reads the durable operation record;
3. resolves and revalidates the plan's coordination domain, acquires its exclusive transaction reservation keyed by canonical repository identity, then validates replay semantics and claims a renewable owner lease plus fencing epoch;
4. durably initializes the operation journal and records the exact staging/backup roots plus staging intent before creating any artifact;
5. stages replacement content atomically into that operation-scoped coordination-domain-protected root outside the repository, preferably on the target volume, and journals each artifact's hash and completion;
6. proves that every read dependency, affected path, and source/destination directory entry supports an OS-enforced conditional effect or exclusive external-write guard;
7. establishes complete transaction protection by acquiring that guard set in deterministic order or recording immutable version tokens that every conditional effect checks atomically, and retains it with the repository reservation through final status or explicit recovery handoff;
8. while holding both boundaries, performs final whole-plan preflight of every revision, read dependency, path, hash, permission, link, destination, and free-space condition;
9. before the first repository effect, uses one store-enforced compare-and-swap to either commit `CancelledNoEffect` when cancellation is pending or append `FirstEffectStarted` with the complete first-effect intent, expected intermediate state, and guard/version tokens, making the current fenced owner eligible to request exactly that effect without granting reusable write authority;
10. routes every requested effect through a non-bypassable OS-enforced effect-admission broker/fence that, at the actual filesystem boundary, atomically revalidates the guard tokens, ledger state, fencing epoch, lease, one-use intent, expected intermediate repository state, all prior effect outcomes, and every still-relevant plan dependency, then performs the guarded conditional side effect and durably records its observed outcome before the fence can quiesce;
11. orders operation boundaries deterministically and uses atomic per-path replacement where supported;
12. while still holding the complete guard set, verifies expected postconditions and durably records the terminal result; then cleans staging and releases the guards and reservation; or
13. performs guarded rollback from the journal.

Staging and backups are control-plane artifacts, never target-repository paths. The
operation chooses a coordination-domain-protected, non-overlapping root outside the
checkout and, when
same-volume atomic replacement is required, on the target volume. If it cannot prove
that boundary or create a safe same-volume location, unattended apply is blocked.
The journal is durable before the first staging write and makes staging creation,
hash verification, cleanup, and restart reconciliation idempotent. A crash,
cancellation, or stale-plan rejection before the first repository effect can leave
only journalled control-plane artifacts; startup/status cleanup removes them without
touching the repository and retains the plan until cleanup is verified.

All staging/backup/control-root mutations, guard acquisition/release, and final
preflight work pass through an operation-scoped `PreEffectActivityFence` bound to the
owner and monotonic control generation. The worker has no bypassable direct authority
over the protected control root or guard manager. Cancellation and other no-effect
resolution close effect and pre-effect admission and advance the control generation in
the same authority transaction. Already admitted activity drains; stale generations
cannot create or journal another artifact or acquire a guard.

After `Received`, cancellation targets the canonical repository/operation identity
and is durably appended to the intake receipt or ledger. If the durable receipt has
not yet been promoted, cancellation acquires the same cross-process intake fence and
atomically materializes the terminal ledger; receipt promotion checks that same
state, so it cannot enqueue work after cancellation wins. While no
`FirstEffectStarted` marker exists, cancellation and the owner's first-effect permit
compete through one store-enforced compare-and-swap. If cancellation wins, the
operation durably records `CancelledNoEffect` with the request hash, request and
commit timestamps, journal sequence, proof that no first-effect permit exists, the
staged-artifact inventory, and cleanup status. The same transaction fences the owner
from starting an effect or new pre-effect activity. This applies while waiting for the repository reservation,
staging, acquiring external-write protection, or running final preflight.

Control-artifact cleanup after `CancelledNoEffect` is idempotent and remains outside
the repository. The terminal result may report `CleanupPending`; startup and status
lookup may inventory/remove artifacts and append `CleanupVerified` only after the
pre-effect fence is closed and drained, every guard is released, and an exclusive
rescan proves the operation root empty. Unreachable activity state fails closed as
pending. The exact plan, journal, artifact inventory, quota reservation, and cleanup
capability remain pinned until that verification. During its retained
epoch window, replaying the same repository ID, operation ID, and request hash returns
the same cancellation result; after epoch compaction it returns
`OperationEpochRetired`. Neither path restarts execution. A new attempt requires a
current-epoch operation ID, while a different payload under a retained ID remains
`OperationConflict`.

If the owner wins the compare-and-swap, it durably appends
`FirstEffectStarted` as the complete journal intent for the first guarded conditional
effect before issuing it. The marker conservatively means that effect may have run;
after owner loss, reconciliation proves its observed outcome from the recorded
precondition, guard tokens, and filesystem evidence rather than assuming it ran or
did not run. Any later cancellation is recorded but cannot produce
`CancelledNoEffect` or interrupt recovery-critical verification and rollback.
Rollback is best-effort across
platform/filesystem constraints and is not falsely described as atomic. If rollback
cannot restore every verified precondition, the operation enters the durable
intervention state `RecoveryRequired`, not `RolledBack` or `Failed`. Journals,
backups, affected paths, observed hashes, and deterministic recovery instructions
are retained until successful recovery or explicit acknowledgement.

Stale-plan rejection is permitted only when drift is detected before the first
repository effect. If revision, dependency, path, identity, hash, permission, link,
or other declared precondition drifts after any effect has been journalled, the owner
records the observed drift, performs no remaining planned effect, and enters the
verified rollback path. Complete verified restoration ends as `RolledBack`; any
unrestored or ambiguous path ends as `RecoveryRequired`. A mid-apply drift is never
reported as `RejectedStale` or `Applied`.

### External-writer exclusion at commit time

The repository transaction reservation serializes Roslyn Tools operations only; it
does not by itself exclude an editor, build, script, or other process. Final preflight
therefore runs only after an external-write guard set covers every reviewed read
dependency, affected object, and relevant source/destination directory entry. Each
guard must either prevent non-cooperating processes from changing the validated
identity, content, metadata, or directory entry until durable outcome, or provide an
atomic conditional filesystem effect that commits only while all reviewed version
tokens still match. Advisory locks, a check followed by ordinary rename, and atomic
replacement without a compare condition do not satisfy this requirement.

The guard set is acquired in deterministic path order and is held through journal
intent, apply, rollback/recovery effects, final verification, and durable outcome.
Creates use exclusive name reservation; replace, delete, move, permission, and
restore operations bind validation to the exact guarded object and directory entry.
If a guard cannot be acquired, is lost, or reports drift, no unguarded effect occurs:
before the first effect the operation rejects stale/unsupported input, and afterward
it follows guarded rollback to `RolledBack` or `RecoveryRequired`.

A platform/filesystem combination that cannot exclude or condition on external
writers for the complete plan blocks unattended apply during preflight. Any future
explicit degraded interactive mode requires a separate reviewed contract and cannot
be exposed as stable apply-capable MCP behaviour.

### Manual recovery and acknowledgement

`RecoveryRequired` is not terminal. It is a durable recovery barrier in the
repository's coordination domain and cross-principal registry: new apply operations
from every OS principal
are rejected before final validation until the operation reaches a verified recovered
state or an authorized user explicitly acknowledges the unresolved condition.

`resume` and `rollback` resolve the operation by canonical repository ID plus
operation ID, then acquire the same exclusive repository transaction
reservation and complete external-write/conditional-effect protection, compare-and-swap the current
state to `Recovering`, and claim a new owner lease and fencing epoch before touching
the repository. They operate only from
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

Source and destination paths are contained under the canonical repository root;
staging, backup, and control paths are contained under their
coordination-domain-protected root and
must not overlap the repository. String normalisation, ordinary revalidation, and advisory locks are necessary but
insufficient. Every
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
identity in the repository's coordination domain. A live owner renews its lease while applying or rolling back and holds the
OS-enforced reservation continuously from final whole-plan preflight through the
durable terminal result or explicit recovery handoff. Other apply/recovery requests
for the same canonical repository wait, cancel, or reject before final validation;
their operations cannot interleave.

On process startup and `GetOperation`, a reconciler must acquire that same
reservation before a compare-and-swap advances the epoch and transitions an expired
`Applying` entry to `Reconciling`; process exit releases the OS reservation. If an
expired owner still holds it, the reconciler performs no repository effect and does
not claim the epoch until the reservation is safely released. Every repository effect
is mediated by a non-bypassable OS-enforced broker/fence; `FirstEffectStarted` only
authorizes a one-use request to that broker. Effect admission and fence closure are
linearizable, and a fence is not drained until an admitted conditional effect and its
observed journal outcome complete. A worker with direct repository-write handles does
not meet this contract.

The supervisor applies a bounded shutdown to the mutation worker. If it cannot prove
worker exit and all write capabilities closed, or prove that the effect fence is closed
and drained, it does not revoke or advance the epoch. It appends nonterminal
`OwnerQuiescencePending` with the possibly in-flight journal boundary and retains the
reservation plus an authority-wide intervention barrier that blocks reconciliation
effects, acknowledgement, transfer, retirement, and queued plans. Only verified
termination or a reconciled non-bypassable fence permits epoch advancement and entry
to `Reconciling`; `RecoveryRequired` is unavailable while the stale holder may still
execute. After safe takeover, a resumed old owner cannot reacquire the
reservation with its stale token and must stop before journalling or performing
another side effect. Fault injection suspends owners before and after reservation,
epoch, and effect boundaries to prove this property.

Before a reconciler performs any repository effect, it also reacquires and validates
the complete external-write/conditional-effect protection from the persisted plan.
If that protection cannot be re-established, it records `RecoveryRequired` and performs no unguarded resume,
rollback, replace, move, delete, or restore.

The reconciler verifies the plan hash, journal, backups, staged files, affected-path
identities, and observed pre/post hashes. If the intact journal proves no
`FirstEffectStarted` permit exists and no repository effect was possible, it reruns
final preflight and may terminate as `CancelledNoEffect`, `RejectedStale`,
`RejectedUnsafeCoordination`, or `Expired`; it does not roll back unrelated external
drift. Otherwise it deterministically completes a provably finished apply, resumes or
rolls back only from a declared journal boundary, or enters `RecoveryRequired` with
retained evidence. An expired operation is not left or reported as a healthy live
`Applying` owner: when stale-writer quiescence cannot yet be proven it remains
explicitly `OwnerQuiescencePending` behind the intervention barrier. Ambiguous state
is never reported as `Applied` or silently replayed.

## Transport outcome

Transport reports what the caller can prove about dispatch:

- `NotDispatched`: no request bytes were accepted; an automatic retry may be permitted by operation policy.
- `Completed`: a valid terminal or current operation response was received.
- `Indeterminate`: dispatch may have occurred but no valid response is available.

`Indeterminate` never triggers an automatic mutation retry or in-process fallback.
The host surfaces the caller-known `OperationId` and queries `GetOperation`. A
`Received` receipt or operation state is reconciled in place. Only an authoritative,
durably recorded `NotRecorded` result permits explicit resubmission of the identical
request with the returned replay epoch, and only before the operation epoch seals;
this continues the same operation identity and fences the delayed original attempt.
An unknown ID from a sealed epoch is non-replayable, and an ID at or below the
retirement watermark returns `OperationEpochRetired`; either result requires a new
current-epoch operation ID for a new attempt. Read-only operations may have a
separately declared retry policy, but a retry still uses the same request context and
operation contract.

Cancellation before durable `Received` remains transport knowledge and does not
manufacture an operation result. If cancellation wins before any request bytes are
accepted, transport may return `NotDispatched`. Once any request bytes are accepted,
cancelling or timing out the caller's wait returns `Indeterminate`, never an ordinary
cancelled operation result, even when `Received` has not yet been committed. The
receiver treats transport cancellation as cancellation of the response wait, not
permission to abandon the intake critical section silently.

The caller retains the same operation ID, sends no new apply, and resolves status
through `GetOperation`. The receiver's receipt creation and the resolver's
authoritative `NotRecorded` transition contend under the same intake fence: if
`Received` wins, status exposes that canonical operation; if `NotRecorded` wins, the
delayed receiver is rejected. While a receiver lease remains in flight, status remains
indeterminate rather than inferring absence. A receiver that enters intake registers a
bounded renewable `IntakeAttempt` lease under the fence; cancellation stops response
waiting, not the lease protocol, and status may create `NotRecorded` only after the
lease is absent/expired. A receiver arriving after `NotRecorded` cannot obtain intake
without the one-use replay epoch. If the caller intended to cancel the
work and status finds durable `Received`, it invokes idempotent `CancelOperation` for
that identity; receipt promotion and cancellation then use the existing fenced
`CancelledNoEffect`/`FirstEffectStarted` arbitration. If status returns authoritative
`NotRecorded`, no canonical operation exists to cancel and only the guarded exact
replay rules apply. This race can never authorize a new operation ID automatically.

Once acceptance is durable, cancellation is part of the canonical operation: status
returns its current state or `CancelledNoEffect`, including after the original
transport response is lost.

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

Paging state is host-neutral and revision-bound. An opaque integrity-protected
continuation token binds the schema/operation, canonical request hash (with the token
omitted), repository/workspace identity, source revision and semantic-input/evaluator
fingerprint, projection/filter/order/tie-break policy, page-size policy, cursor, and
expiry. Every page is reauthorized and either reads the exact retained immutable
snapshot under bounded quotas or returns a typed invalid/stale/expired/unauthorized
continuation error. The token is not a bearer grant, cannot elevate trust, and never
causes reevaluation against a later workspace revision. Operations that cannot retain
a safe bounded snapshot return explicit non-resumable truncation.

## MCP and agent surface

The preferred implementation is the official C# MCP SDK. A handwritten host is acceptable only if spawned-process conformance tests prove every advertised protocol version and capability.

Stable MCP tools:

- negotiate or explicitly select a supported protocol version;
- obey request/notification rules and never answer notifications;
- support cancellation for in-flight work and progress for long-running operations;
- expose schema-valid `structuredContent` plus an optional human text view;
- use complete JSON Schema for enums, bounds, nullability, nested values, and additional-property policy;
- bound request size, output size, item count, execution time, concurrency, and memory-sensitive operations;
- paginate large results through revision-bound integrity-protected continuations, or project/truncate them explicitly without a resumable token;
- classify tools as `stable`, `preview`, or `experimental`; and
- hide apply-capable bridge tools from the stable surface.

Capability metadata generates or verifies CLI help, MCP schemas, `prime`, aliases/defaults, safety, examples, and follow-on actions. CI executes every published example against fixtures.

## Transparent daemon boundary

For this phase the daemon is optional typed transport only. It:

- accepts the same typed request the in-process router accepts;
- invokes the same operation and returns the same typed envelope;
- uses a per-user, collision-resistant endpoint that mutually authenticates the client principal and daemon instance before request bytes are accepted;
- preserves request/operation IDs, cancellation, diagnostics, provenance, confidence, and safety state; and
- may be bypassed before dispatch when unavailable.

It has no workspace cache, global current-directory semantics, command-line argument bridge for mutations, output capture, idempotency authority, retry policy, or command-specific semantic mapping. It does not log raw request payloads; structured logs use redacted fields and bounded diagnostics.

Endpoint authorization uses OS-enforced peer identity and endpoint/container ACLs, or
a cryptographically equivalent mutually authenticated local channel with user-private
OS/service-protected discovery and credentials outside repository control. Server
credentials are instance-bound and rotate on restart; they never travel in endpoint
names, arguments, environment, logs, repository files, or request payloads. If any
endpoint, discovery, credential-protection, mutual-authentication, or authorization
boundary cannot be proven, the daemon does not start or advertise an endpoint and the
host may choose direct execution only before dispatch.

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
