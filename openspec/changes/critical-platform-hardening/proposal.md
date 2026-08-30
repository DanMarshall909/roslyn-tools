# Critical platform hardening

- Status: proposed
- Priority: P0/P1
- Decision owner: architecture
- Supersedes for this phase: daemon workspace-caching work
- Reviewed implementation baseline: `main` at `6817221d3db5b28f94ee72183be0cb0b8bff984e`

## Why

The current tool is valuable, but its advertised execution safety, protocol maturity, distribution status, and cross-host parity exceed the guarantees enforced by the runtime.

The most serious gaps are:

- repository-controlled MSBuild evaluation is performed without an explicit trusted-workspace boundary or isolated evaluator;
- legacy daemon routing can still fall back in-process after a mutation may have been dispatched;
- the staged writer provides in-process compensating rollback for supported changes, but plans, journals, status, and failed-rollback evidence do not survive restart and path containment is not protected atomically against symlink/reparse races;
- incomplete workspace loads can produce proof-like findings and deletion advice;
- the handwritten MCP host does not conform to the lifecycle and message rules of the versions it advertises, and does not expose real structured results;
- Debug builds mutate the working tree and user profile, while release metadata advertises `1.0.0` despite no corresponding release.

The typed RPC recovery coordinator, `PreparedChangeSet`, staged compensating writer,
host-neutral follow-on metadata, and command-migration ratchet now on `main` are
useful foundations. The writer preflights and stages supported changes, backs up
originals, suppresses cancellation during commit, and attempts reverse-order
rollback. It remains a process-local foundation rather than completion of the
durable safety kernel.

## Scope

This change establishes:

- an explicit trusted-workspace policy and an isolated execution boundary for repository-controlled MSBuild, analyzers, and generators;
- a prepared-change-set mutation safety kernel with durable operation identity, idempotency, status lookup, rollback, and recovery;
- a `complete | partial | failed` workspace contract with provenance and shared confidence policy;
- a canonical typed execution spine for `outline`, `find-refs`, `find-unused`, `rename`, and `safe-delete`;
- a conformant, versioned MCP surface with structured content, bounded resources, and executable schemas;
- catalogue-derived metadata, examples, and command admission rules;
- hermetic build, dependency, CI, installer, and release gates;
- a temporary daemon constrained to transparent transport passthrough.

## Non-goals

- Implementing a daemon-owned workspace cache, session manager, incremental invalidation, or multi-repository service.
- Broadly converting all commands before the representative vertical spine is complete.
- Replacing discoverable typed tools with a generic untyped execution endpoint.
- Claiming that a multi-file filesystem transaction can be perfectly atomic on every platform. The required guarantee is deterministic preflight, journalling, rollback where possible, and an explicit `RecoveryRequired` state where it is not.
- Automatically trusting a checkout because it is local or because the user opened it in an editor.

## Decision

The daemon is not an execution authority in this change. It is an optional transport adapter that forwards a typed canonical request and returns the canonical result without semantic change. It must not own workspace state or mutations, retry after indeterminate mutation dispatch, manufacture fallback execution, use process-global request context, log raw payloads, or change diagnostics, result shape, cancellation, provenance, or safety policy.

Before dispatch, every apply caller obtains a bounded authenticated repository-scoped
operation-epoch token and constructs a caller-known operation ID from its monotonic
epoch plus a fresh nonce. The operation epoch is independent of owner fencing and
one-use replay authorization; the repository/principal-bound token is presented on
apply. Canonical repository ID plus operation ID is the durable
idempotency, status, and recovery handle. A separate request-attempt ID may be used
for transport correlation, but an operation ID first returned by the server after
dispatch is insufficient.

Prepare computes plan hash over a versioned host-neutral canonical payload with the
hash field omitted entirely, then atomically persists that exact payload and claimed
hash in protected storage outside the target repository. It returns the repository
ID, plan ID, and plan hash. Storage
is user-private only when `ExclusivePrincipal` ownership is proven; shared writable
repositories require an authority-owned coordination domain. Apply
loads those exact bytes across process restart; it never recomputes the reviewed work
from command arguments or a newer workspace snapshot.

Apply durably journals staging intent before creating replacement content. Staging,
backups, and control artifacts remain in an operation-scoped protected root outside
the repository, on the target volume when required, so pre-effect cancellation,
crash, or stale rejection can leave only restart-cleanable control artifacts and no
unjournalled repository write.

Mutation coordination is repository-scoped across OS principals and hosts. An OS-
or service-protected registry outside repository control supplies one non-reused repository identity and
records ownership, active recovery barriers, and retirement state for every would-be
writer; machine-local state is valid only when other hosts cannot write the storage.
For a new checkout, a separately authorized `EnrollRepository` operation verifies
handle-based checkout/Git-common-dir identity and, under one enrollment fence,
atomically advances a continuity-protected authority sequence and persists the full
binding/domain/no-barrier/audit record. Mutation never enrolls implicitly. A healthy
absent mapping returns `RepositoryNotEnrolled`; lost, rolled-back, or inconsistent
authority state remains `RejectedUnsafeCoordination`, and concurrent enrollment
returns one identity without sequence reuse.
A plan must then prove either that no other non-administrative principal or host in
the threat model can write/replace the checkout and Git common directory, or that
every authorized principal uses one OS/service-enforced shared authority for intake, reservation,
state, recovery barriers, fencing, bounded operation epochs/outcomes, the retirement
watermark/accumulator, and pinned records. Otherwise mutation is refused as
`RejectedUnsafeCoordination`. `RecoveryRequired` is committed to the authority and
cross-principal registry before reservation release, and ownership transfer cannot
discard its barriers, epoch state, or pinned records. Transfer preserves repository
identity and first enters `TransferPending` to block new intake; it waits for or
reconciles every active operation under the old reservation, resolves/acknowledges all
barriers, proves the repository quiescent, then atomically rotates ownership,
credentials, and `CoordinationGeneration`. A crash never exposes two writer domains.

The authority caps open and unretired epochs, issuance rate, accepted operations and
bytes, live and retained records, and pinned exceptions. Admission reserves bounded
worst-case ledger, outcome, and recovery-metadata space, then returns
`CoordinationCapacityExceeded` before intake when capacity is unavailable. Status,
recovery, and already accepted operations remain able to record truthful state.

After durable intake, cancellation belongs to the canonical operation. Before the
first repository effect, one store-enforced compare-and-swap either records terminal
`CancelledNoEffect` with a no-effect proof and cleanup inventory or appends
`FirstEffectStarted` with the complete first-effect intent and guard/version evidence,
making the fenced owner eligible to request exactly that effect through a
non-bypassable OS-enforced broker that revalidates authority at the actual conditional
filesystem boundary. The permit is not reusable direct write authority, and expired
owner takeover cannot advance the epoch until the effect fence is closed and drained
or worker termination and write-capability closure are verified.
Replay returns the cancellation outcome while its bounded record is retained and
`OperationEpochRetired` after safe epoch compaction; neither restarts work. Once the
first-effect permit wins, cancellation is recorded but recovery-critical verification
and rollback continue to a truthful repository outcome.

Before durable intake, cancellation remains transport knowledge. It is
`NotDispatched` only when no request bytes were accepted; otherwise the caller receives
`Indeterminate`, retains the same operation ID, and resolves `Received` versus
authoritative `NotRecorded` under the intake fence before targeting canonical
cancellation or considering guarded exact replay. It never starts a new operation ID.

Terminal outcomes remain addressable for a minimum replay window. After an epoch is
sealed, unknown IDs return `OperationEpochSealed` without intake or replay
authorization. Eligible terminal digests are folded into a cumulative accumulator and one
monotonic `RetiredThroughEpoch` watermark advances before individual records are
removed. Bounded live/recovery exceptions remain pinned. Any delayed unknown request
from a retired epoch returns `OperationEpochRetired`, never `NotRecorded` or execution,
so replay safety no longer requires one permanent record per operation.

Repository retirement first enters `RetirementPending`: new mutation and transfer are
blocked while status/recovery remain available, all epochs seal, active operations
quiesce, recovery finishes or is acknowledged, cleanup verifies, pinned records release, and
retained outcomes compact through the final issued epoch. Only then can the live
mapping become a permanent retired marker. Retirement can never strand an operation's
only status/recovery key.

A failed or interrupted rollback is never reported as clean rollback. The operation enters durable intervention state `RecoveryRequired`; its journal, backups, diagnostics, and deterministic recovery guidance are retained until recovery or explicit acknowledgement. Recovery appends `Recovering` and ends as `RecoveredApplied`, `RecoveredRolledBack`, or another evidence-bearing `RecoveryRequired`. `RecoveryAcknowledged` is an explicit administrative handoff and never claims the repository is clean.

Every `Applying` record has a renewable owner lease. Expired owners transition
through bounded `Reconciling` work on startup or status lookup. An intact journal with
no `FirstEffectStarted` permit can prove no repository effect, allowing revalidated
no-effect cancellation, stale, unsafe-coordination, or expiry outcomes without
rolling back external drift. Otherwise reconciliation finishes only as a verified
apply, verified rollback, or `RecoveryRequired`. A monotonically increasing
fencing epoch and one OS-enforced exclusive repository transaction reservation,
held from final preflight through durable terminal completion or recovery handoff,
prevent overlapping plans and stop a resumed superseded owner after takeover.
Filesystem effects use
handle-relative no-follow/reparse-safe operations and revalidate hard-link identity
and count. Unattended apply is blocked when the platform cannot prove containment or
cannot prevent an undeclared shared-inode effect at the side-effect boundary.

Plans referenced by nonterminal (including `Recovering`) or `RecoveryRequired`
operations, and `CancelledNoEffect` plans with pending control-artifact cleanup,
remain pinned with their evidence until verified terminal cleanup or explicit
acknowledgement; normal retention cannot strand recovery without the reviewed bytes.

Repository trust and evaluator mode are independent. Untrusted repositories are not evaluated inside the long-lived CLI, MCP, or daemon host, while trusted repositories may still request isolation. An isolated worker receives an OS-enforced read-only checkout and separate disposable writable output/cache locations; the tool must refuse evaluation when that boundary is unavailable.

The isolated worker's filesystem namespace is allow-listed, not merely writable-safe:
only the checkout, declared SDK/package/tool inputs, and disposable output locations
are visible. Host profiles, credentials, SSH material, devices, and undeclared caches
remain outside the namespace.

Untrusted `NoEvaluation` operations are also contained. Host-side file reads use one
verified-root, handle-relative, no-follow/reparse-safe reader with final-handle
identity checks, declared regular inputs, and conservative link/special-file policy.
Any input that could redirect to ambient host data is denied, routed to the
allow-listed worker, or causes a typed refusal; redirected content never reaches
results or diagnostics.

## Success measures

- No apply-capable legacy daemon or MCP bridge can automatically replay a mutation after dispatch ambiguity.
- A caller obtains an authenticated bounded operation epoch and constructs the operation ID before dispatch; after a lost response it can query a durable receipt/current outcome, obtain authoritative `NotRecorded` replay only while that epoch remains open, or receive a non-replayable sealed/retired result once it closes.
- Operation retention is bounded by epoch/time/count/byte and pinned-exception quotas; compaction advances one monotonic rejection watermark/accumulator before deleting individual outcomes, so aged-out work cannot execute and the store does not grow one permanent record per operation.
- Repository retirement preserves status and recovery until every active, recovery, cleanup, pinned, and retained record is resolved and compacted; only a fully quiescent identity can become a reject-only marker.
- A shared writable checkout either has one enforced cross-principal/cross-host coordination authority or remains inspect-only; no second OS user or host can bypass `RecoveryRequired` through separate private state.
- A new checkout can be enrolled explicitly under an authority fence, while lost registry continuity can never masquerade as first enrollment or reset a repository identity.
- Ownership transfer preserves identity and cannot rotate the writer domain until all intake, reservations, active operations, and recovery barriers are fenced and quiescent.
- An untrusted file-only operation cannot use a repository symlink, reparse point, mount, hard link, alternate stream, or special file to read undeclared host content in a privileged process.
- Cancellation after durable intake either proves `CancelledNoEffect` before the first-effect permit or records the request while recovery-critical work reaches a truthful repository outcome; replay never restarts a cancelled operation.
- Cancellation after request-byte acceptance but before durable intake is `Indeterminate` and must resolve the same identity under the intake fence; it cannot be mistaken for a safe ordinary cancellation or authorize a second apply.
- Review and apply can run in separate processes and still consume byte-identical, hash-verified plan data from outside repository control.
- `PlanHash` has one cross-adapter canonical representation and never hashes itself.
- Plan retention cannot remove bytes still referenced by a nonterminal (including recovering) or recovery-required operation.
- A symlink/reparse swap after preflight cannot redirect any write, move, delete, backup, or restore outside the verified root.
- A hard-linked path cannot mutate an undeclared shared inode or silently lose a reviewed link relationship.
- A non-`rt` writer cannot change a reviewed dependency or target between validation and effect; apply uses an OS-enforced guard or atomic conditional effect and blocks unattended execution where neither exists.
- Killing the apply owner after any journal boundary leads through lease-based reconciliation to a verified terminal state rather than indefinite `Applying`.
- A superseded owner resumed after lease takeover is fenced from every further repository side effect.
- Pre-effect drift is rejected without writes; drift after a journalled effect stops the remaining plan and produces verified rollback or `RecoveryRequired`, never stale rejection or applied success.
- Overlapping apply/recovery plans for one repository cannot interleave between final preflight and durable terminal state, and `RecoveryRequired` blocks unrelated apply until verified recovery or explicit acknowledgement.
- An owner lost before `FirstEffectStarted` can reconcile stale preflight to `RejectedStale` only with durable no-effect proof; ambiguous or possibly effected state never masquerades as stale or rolled back.
- Fault injection proves complete success, complete rollback, verified post-recovery state, or truthful `RecoveryRequired`; partial application is never labelled `Applied`.
- No workspace is `Complete` unless every operation-required project, compilation context, reference, build input, document, and generator/analyzer stage is accounted for; no proof-strength finding or deletion suggestion is emitted from partial input or an externally visible symbol without an explicit proof basis.
- Trust/evaluator tests cover untrusted isolated evaluation and trusted opt-in isolation; repository-controlled build logic cannot write through the isolated worker's read-only checkout, read undeclared host files, use network egress, or execute in a privileged long-lived host, and dependency acquisition is a separate trusted pre-evaluation step.
- The five spine commands pass CLI, MCP, in-process, and passthrough parity plus cancellation and failure-injection tests.
- MCP conformance is tested for every advertised protocol version; structured tools return schema-valid `structuredContent` as well as a text view.
- `dotnet build` is hermetic: it does not modify the repository, deploy files into the user profile, or depend on an already-running daemon.
- Release verification installs and runs every supported RID, verifies checksums/provenance and rollback-safe upgrades, and either publishes the advertised NuGet tool or removes that claim.
