# Platform hardening specification

## ADDED Requirements

### Requirement: canonical operation parity

Each stable command SHALL have one typed request, one application operation, and one typed result/error envelope. CLI, MCP, in-process execution, and the passthrough daemon SHALL call that operation without reconstructing CLI arguments, capturing console output, parsing rendered text, or introducing host-specific safety semantics.

#### Scenario: converted command runs through every host

- **WHEN** a supported request for a converted command is submitted through CLI, MCP, in-process, and passthrough daemon adapters
- **THEN** each outcome has equivalent status, data, diagnostics, provenance, confidence, safety state, and affected-path ordering

#### Scenario: stable command lacks a canonical operation

- **WHEN** a command is proposed for the stable CLI or MCP surface without a typed application operation and result contract
- **THEN** command admission fails in CI

### Requirement: repository evaluation has an explicit trust boundary

A workspace request SHALL declare repository trust state and evaluator mode as independent values. Trust state SHALL be `Trusted` or `Untrusted`; evaluator mode SHALL be `NoEvaluation`, `InProcess`, or `IsolatedWorker`. Before evaluation, the application boundary SHALL derive the effective trust state from an OS/service-protected, authenticated-principal-scoped trust registry outside repository control, keyed by canonical repository identity and its verified physical binding. A request-carried trust value is only an expected-state or downgrade constraint and SHALL NOT grant or elevate trust. Absence, mismatch, revocation, stale physical binding, or inability to read the authoritative decision SHALL produce `Untrusted`.

Only explicit authenticated `GrantRepositoryTrust` and `RevokeRepositoryTrust`
operations backed by deliberate user action through a trusted surface SHALL mutate the
registry; repository content, an evaluator, an ordinary workspace request, or an
adapter SHALL NOT synthesize that authority. Grant/revoke SHALL be durably audited,
and non-interactive operation without a pre-existing valid grant SHALL default to
`Untrusted`.

The trust authority SHALL enforce authority-wide and per-authenticated-principal
count/byte limits for `Trusted`, `RevocationPending`, and bounded hot `Untrusted`/audit
records plus grant rate/concurrency limits. Before persisting `Trusted`, a grant SHALL
atomically reserve the record, audit, and worst-case eventual revocation/compaction
capacity. Exhaustion SHALL return typed `TrustRegistryCapacityExceeded` before trust is
granted; the binding SHALL remain `Untrusted`, and lookup, evaluator termination,
reconciliation, and revocation of admitted grants SHALL remain available. Revoke SHALL
consume reserved capacity, SHALL NOT be blocked by grant quota/rate, and SHALL be
idempotent without appending another durable transition when the binding is already
authoritatively untrusted.

Every trust transition SHALL receive a never-reused monotonic `TrustDecisionSequence`.
After an `Untrusted` revocation is complete, its old-generation evaluator domains are
proven empty, and its declared hot-retention window elapses, the authority SHALL first
fold the record and audit digest into a fixed-size authenticated
`TrustAuditAccumulator` and advance `CompactedThroughTrustDecision` in one
continuity-protected transaction, then MAY remove the per-binding detail. Absence after
compaction SHALL remain `Untrusted`; a restored decision at or below the watermark
SHALL be rejected, and only a new deliberate grant with a later sequence MAY establish
trust. Current `Trusted` or `RevocationPending` state and any generation with an
unquiesced lease SHALL remain pinned. Hot audit count/bytes SHALL be bounded; older
detail MAY move to a separately quota-bound archive or be deleted only after the
accumulator commit. Capacity changes SHALL be explicit authenticated audited
administrator operations. Startup and restore SHALL prove continuity of the authority
generation, decision-sequence high-water mark, compaction watermark, and accumulator;
failure SHALL refuse grants and resolve evaluation as `Untrusted`, never as an empty
fresh authority.

Grant, revoke, and every admission capable of starting repository-controlled code in
`InProcess` SHALL share one linearizable trust gate keyed by authenticated principal,
canonical repository identity, and verified physical binding. Evaluator admission
SHALL atomically re-read the current grant and acquire a revocable evaluation lease
bound to its monotonic trust generation and a held verified-root capability before any
repository-controlled code starts. The capability SHALL pin the exact physical root
named by the grant and SHALL provide the evaluator a handle-relative or immutable
snapshot view; evaluator startup SHALL NOT re-resolve an ambient path that can name a
replacement checkout. The actual solution/build open boundary SHALL consume that
capability and revalidate its identity before marking evaluation started. A platform
or evaluator API that cannot preserve the binding through that open SHALL refuse
`InProcess` and use `IsolatedWorker` or fail safely.

Before `InProcess` admission, an external supervisor that executes no repository code
SHALL place the evaluator host and every process it can create in a non-breakaway,
OS-enforced descendant domain and acquire a generation-bound containment lease. The
host, evaluated code, and descendants SHALL NOT detach, daemonize, re-parent, create a
process outside, disable, or move out of that domain. The supervisor SHALL retain an
out-of-domain kill capability and durable status path. Revocation SHALL first request
cooperative cancellation and then terminate the per-admission domain or, when safe
per-admission attribution is unavailable, the entire containing host domain. It SHALL
remain `RevocationPending` until the OS proves the revoked generation has no live
descendant. Other operations interrupted by whole-host termination SHALL receive their
typed interrupted/indeterminate outcomes; they SHALL NOT justify leaving a descendant
running. A platform that cannot prove non-escape, external termination, and empty-domain
quiescence SHALL refuse `InProcess`.

A prior read of `Trusted` SHALL
confer no admission authority. Revocation SHALL
atomically close new `InProcess` admission, advance the trust generation, persist
`Untrusted`, and durably audit that transition. A request that observed the former
grant but had not acquired its lease before that transition SHALL be denied.

An evaluation whose lease linearized first is already admitted. Revocation SHALL
cancel or terminate every such old-generation in-process evaluation and its
descendants, and SHALL NOT report completion until the authority verifies that they
are quiescent and cannot execute more repository-controlled code. Until then the
repository remains `Untrusted`, new `InProcess` admissions and grants remain fenced,
and the revoke operation SHALL expose a typed `RevocationPending` outcome recoverable
through status/reconciliation. Missing or unreachable lease state SHALL fail closed;
it SHALL NOT be treated as quiescence. An
untrusted request SHALL NOT select `InProcess`, while a trusted request MAY still
select `IsolatedWorker`. The long-lived CLI, MCP, and daemon hosts SHALL NOT evaluate
repository-controlled MSBuild logic, analyzers, generators, or repository executables
in-process for an untrusted repository. `IsolatedWorker` SHALL use an OS-enforced
allow-listed filesystem namespace that exposes only declared read-only inputs and
disposable writable outputs; it SHALL NOT expose ambient host paths or credentials.
Before exposure, the supervisor SHALL open checkout inputs relative to the verified
root with no-follow/reparse-safe primitives, validate final-handle identity and type,
and construct an immutable per-operation view. A multiply linked regular file SHALL
be rejected without reading its contents unless every alias is proven inside the
declared roots. Accepted inputs SHALL be materialized onto private inodes that cannot
alias host filesystem objects, or mounted only when the platform proves equivalent
alias containment for the snapshot lifetime. An unresolved live checkout inode SHALL
NOT be bind-mounted into the worker, and bytes from a rejected external alias SHALL
NOT enter results, diagnostics, logs, or caches.

Every `IsolatedWorker` phase capable of executing untrusted repository-controlled code
SHALL run under a distinct least-privilege security identity that cannot authenticate
as or impersonate the host user. OS-enforced process/PID, process-handle, and IPC
isolation SHALL prevent the worker and its descendants from enumerating, opening,
debugging, signalling, injecting into, or exchanging undeclared IPC with host or other
same-machine processes. The worker SHALL inherit no ambient handles and SHALL expose
only bounded schema-validated supervisor channels created specifically for that
operation; child processes SHALL remain inside the same containment and resource
limits.

The supervisor SHALL reserve aggregate host capacity before admission and enforce
per-operation OS limits inherited by every descendant for live and total process/task
creation, threads, process-table entries, open handles/file descriptors, CPU, memory,
wall-clock time, and IPC/result bytes. Every disposable output, intermediate, cache,
and temporary filesystem SHALL have enforced byte and inode/file-count quotas; quota
exhaustion SHALL stop the evaluation with a typed resource-limit outcome rather than
consume unbounded host capacity. On success, failure, cancellation, timeout, or host
reconciliation, the supervisor SHALL terminate all descendants, revoke channels, and
destroy or quarantine every writable namespace within a bounded cleanup deadline.
Unverified cleanup SHALL block reuse of its reserved capacity and SHALL NOT be counted
as successful isolation.

The worker SHALL also run in an OS-enforced networkless namespace with no DNS, socket,
loopback, proxy, or external egress; policy SHALL NOT enable networking inside that
phase. Required SDK/package/tool acquisition SHALL occur beforehand through a
separate trusted broker
that executes no repository code, accepts only policy-allow-listed registries and
user-reviewed lockfile-pinned coordinates, rejects repository-generated URLs, proxy
settings, restore hooks, and dynamic evaluator output, verifies content hashes and
signatures, and exposes results as declared read-only worker inputs. Missing inputs
SHALL produce a typed diagnostic rather than trigger restore inside the worker. A
platform that cannot prove filesystem, process/handle, IPC, identity, and egress
containment SHALL refuse untrusted evaluation.

`NoEvaluation` SHALL NOT mean unrestricted host reads. For an untrusted repository,
every host-side repository read SHALL use one handle-relative reader rooted at the
verified checkout, open every component with no-follow/reparse-safe semantics, bind
the final handle to the expected filesystem identity, and accept only declared
regular-file/directory inputs. Symlinks, junctions, mount points, reparse points,
special files, alternate streams, and hard-linked files whose aliases cannot all be
proven inside declared roots SHALL be denied in-process. A repository-supplied path
SHALL NOT authorize an ambient host input. An operation that cannot establish this
boundary SHALL run inside the allow-listed `IsolatedWorker` namespace or be refused.

#### Scenario: untrusted repository requests semantic solution load

- **WHEN** an untrusted repository requires MSBuild-backed semantic loading
- **THEN** the tool refuses the operation or runs evaluation through the configured constrained worker
- **AND THEN** the result independently reports `Untrusted` trust state and the selected evaluator mode in provenance

#### Scenario: trusted repository requests isolated evaluation

- **WHEN** a trusted repository explicitly selects isolated evaluation
- **THEN** evaluation runs in `IsolatedWorker` without changing the reported trust state
- **AND THEN** provenance independently reports `Trusted` and `IsolatedWorker`

#### Scenario: isolated repository code attempts a write

- **WHEN** repository-controlled build logic, an analyzer, or a generator in `IsolatedWorker` attempts to modify source or repository-control files
- **THEN** an OS-enforced read-only repository view denies the write
- **AND THEN** build outputs, intermediate files, caches, and temporary files use separate disposable writable locations outside the checkout
- **AND THEN** the repository remains unchanged and the denied attempt is reported in provenance

#### Scenario: isolated repository code attempts an undeclared host read

- **WHEN** repository-controlled code in `IsolatedWorker` attempts to read a user profile, SSH key, credential store, host configuration, undeclared package cache, device, or other path outside its declared inputs
- **THEN** the OS-enforced filesystem namespace makes that path unavailable and denies the read
- **AND THEN** only the read-only checkout, declared read-only SDK/package/tool inputs, and disposable writable output/cache/temp locations are visible
- **AND THEN** evaluation is refused on a platform that cannot establish this namespace

#### Scenario: isolated checkout input aliases an ambient host file

- **WHEN** an untrusted checkout path is a hard link whose aliases cannot all be proven inside the declared input roots
- **THEN** snapshot construction rejects the file before reading or exposing its contents to the worker
- **AND THEN** accepted checkout inputs use private non-host-aliasing inodes or an equivalently proven alias-contained immutable snapshot
- **AND THEN** ambient target bytes do not enter the worker, results, diagnostics, logs, or caches

#### Scenario: isolated code exhausts process or writable-filesystem resources

- **WHEN** repository-controlled code forks descendants, opens descriptors, or creates output/cache/temp files until an operation or aggregate quota is reached
- **THEN** inherited OS-enforced process/task, descriptor, CPU, memory, time, byte, and inode limits stop the evaluation with a typed resource-limit outcome
- **AND THEN** the supervisor terminates every descendant and destroys or quarantines all disposable writable namespaces within the bounded cleanup contract
- **AND THEN** capacity with unverified cleanup is not reused or admitted as available

#### Scenario: isolated repository code attempts network egress

- **WHEN** repository-controlled MSBuild logic, an analyzer, a generator, or a child process attempts DNS, socket, loopback, proxy, or external network access during untrusted evaluation
- **THEN** the OS-enforced networkless namespace denies the attempt and no checkout or declared input content leaves the worker
- **AND THEN** network access cannot be enabled for any phase in which untrusted repository code executes
- **AND THEN** missing SDK/package/tool inputs produce a typed diagnostic rather than an in-worker restore

#### Scenario: isolated repository code targets a host process or IPC object

- **WHEN** repository-controlled code attempts to inspect, signal, debug, inject into, open a handle to, or communicate through an undeclared pipe, socket, shared-memory object, or other IPC endpoint owned by the host user or another process
- **THEN** the distinct low-privilege identity and OS-enforced process/handle/IPC boundary deny the attempt
- **AND THEN** only the operation-scoped bounded schema-validated supervisor channels are reachable, all descendants remain contained, and the denied attempt is reported without exposing host data
- **AND THEN** evaluation is refused on a platform that cannot prove this isolation

#### Scenario: dependencies are acquired for a future isolated evaluation

- **WHEN** required SDK/package/tool content is absent before the worker starts
- **THEN** a separate trusted broker that executes no repository code may fetch only policy-allow-listed, user-reviewed lockfile-pinned coordinates
- **AND THEN** it verifies content hashes/signatures and exposes the acquired content to the later networkless worker as declared read-only inputs
- **AND THEN** arbitrary repository-generated URLs, proxy settings, restore hooks, and dynamic evaluator output cannot control the broker

#### Scenario: file-only operation is safe without evaluation

- **WHEN** an untrusted repository invokes an operation whose contract requires no MSBuild, analyzer, generator, or repository executable
- **THEN** the operation may execute in the host only through root-contained, handle-relative, no-follow/reparse-safe reads of declared regular inputs
- **AND THEN** every opened input is bound to the expected filesystem identity and hard-link policy before its content is consumed
- **AND THEN** provenance states that no repository-controlled evaluation was performed

#### Scenario: a file-only input redirects to an ambient host secret

- **WHEN** an untrusted `NoEvaluation` input or path component is a symlink, junction, mount, reparse point, alternate stream, special file, or unproven hard link that can expose data outside declared roots
- **THEN** the privileged host does not follow or read it and no content appears in results, diagnostics, logs, or caches
- **AND THEN** the operation is refused or rerouted to an allow-listed `IsolatedWorker` namespace in which undeclared host paths are unavailable

#### Scenario: a path is swapped during a file-only read

- **WHEN** an untrusted repository path changes identity or becomes a link between validation and open/read
- **THEN** handle-relative open or post-open identity validation rejects the input before its content is consumed
- **AND THEN** the operation returns a typed safety error without exposing the redirected target

#### Scenario: trust is inferred from locality

- **WHEN** a repository is local or open in an editor but has no explicit trust decision
- **THEN** it remains untrusted

#### Scenario: an adapter claims trust without an authoritative grant

- **WHEN** a workspace request or host adapter supplies `Trusted` but the authenticated principal's protected registry has no matching current grant for the verified repository binding
- **THEN** the application boundary derives `Untrusted` and refuses `InProcess` evaluation
- **AND THEN** no ordinary request, repository file, or evaluator output can create the missing grant

#### Scenario: trust-registry grant capacity is exhausted

- **WHEN** an authority-wide or per-principal trust-record, byte, rate, concurrency, audit, or reserved-revocation limit is reached
- **THEN** `GrantRepositoryTrust` returns `TrustRegistryCapacityExceeded` before assigning a decision sequence or persisting `Trusted`
- **AND THEN** the binding remains `Untrusted`, and existing lookup, evaluator termination, reconciliation, and revocation remain available
- **AND THEN** the caller cannot bypass the limit by cycling bindings, principals, grant/revoke transitions, or implicit authority reprovisioning

#### Scenario: completed trust revocation is compacted

- **WHEN** a binding is durably `Untrusted`, every old-generation evaluator domain is proven empty, and its bounded hot-retention window elapses
- **THEN** the authority atomically folds the decision/audit digest into `TrustAuditAccumulator` and advances `CompactedThroughTrustDecision` before removing per-binding detail
- **AND THEN** absence continues to resolve as `Untrusted`, and any restored decision at or below the watermark is rejected
- **AND THEN** trust can be re-established only by a new deliberate quota-admitted grant with a later decision sequence

#### Scenario: revocation races evaluator admission

- **WHEN** an `InProcess` request reads a valid grant and pauses before lease acquisition while `RevokeRepositoryTrust` advances the trust generation
- **THEN** the request cannot acquire an evaluation lease and no repository-controlled code starts
- **AND THEN** all later admissions derive `Untrusted` until a new deliberate grant is allowed after revocation quiescence

#### Scenario: revocation encounters already admitted evaluation

- **WHEN** an in-process evaluation acquired an old-generation lease before `RevokeRepositoryTrust` closed admission
- **THEN** revocation cancels or terminates that evaluation and every descendant under the enforceable supervisor boundary
- **AND THEN** revocation remains `RevocationPending`, keeps the repository untrusted, and fences new admissions and grants until verified quiescence
- **AND THEN** successful revocation means no old-generation repository-controlled code can execute afterward

#### Scenario: trusted checkout is replaced during evaluator startup

- **WHEN** `InProcess` admission acquires its trust lease and the canonical checkout directory entry is replaced before MSBuild opens the solution
- **THEN** evaluator startup reads only through the held verified-root capability or its immutable snapshot and never opens the replacement under the old grant
- **AND THEN** the request either evaluates the exact granted physical repository with matching provenance or fails with a typed stale-binding safety outcome before repository-controlled code starts

#### Scenario: trusted in-process evaluation launches a detached child

- **WHEN** repository-controlled code admitted to `InProcess` forks, daemonizes, re-parents, or otherwise attempts to leave a background descendant and repository trust is revoked
- **THEN** the OS-enforced non-breakaway descendant domain keeps the host and every child under the external supervisor's kill capability
- **AND THEN** cooperative cancellation is followed by termination of the per-admission domain or the whole containing host when attribution is not provable
- **AND THEN** revocation completes only after the OS proves the revoked generation's domain empty; a platform unable to prove this refuses `InProcess`

### Requirement: request context is explicit and host-neutral

Repository root, solution path, configuration, trust state, evaluator mode, resource budget, request ID, operation ID, and protected authenticated-principal/action context SHALL travel as explicit request context. Client/host diagnostic metadata SHALL NOT substitute for authenticated-principal context. An operation SHALL NOT depend on or mutate process current directory, arbitrary process environment, global console state, or `Environment.ExitCode` as request context.

#### Scenario: two repositories execute concurrently

- **WHEN** requests for two repositories overlap
- **THEN** each resolves paths and workspace state exclusively from its own request context
- **AND THEN** neither request changes the process context observed by the other

### Requirement: prepared mutation plans are complete and immutable

A mutation SHALL prepare an immutable change set before apply. The plan SHALL
describe every create, replacement, delete, move, project edit, Roslyn document,
additional document, and analyzer-config document it can affect, including
coordination domain/mode and writer-set proof, exact path preconditions, expected
hashes/existence, platform file identity, link count, known hard-link aliases,
encoding/newline/file-mode policy, symlink/hard-link policy, operation-scoped
staging/backup roots outside the repository, OS/filesystem conditional-effect or
external-write-guard requirements, diff, verification plan, expiry, and repository
revision. Before prepare succeeds, the exact canonical plan envelope SHALL be atomically
persisted in protected durable storage outside repository control: user-private for
proven `ExclusivePrincipal`, or authority-owned/access-controlled for
`SharedAuthority`. Apply SHALL identify that payload and envelope by repository ID, plan ID, and
plan hash and SHALL NOT recompute them from command arguments or a newer workspace
snapshot. Once an operation record references a plan, the store SHALL pin its exact
bytes through all nonterminal states and through `RecoveryRequired` until verified
terminal cleanup or explicit recovery acknowledgement. A terminal no-effect state,
including `CancelledNoEffect`, `Expired`, `RejectedStale`,
or `RejectedUnsafeCoordination`, SHALL retain the plan, journal, artifact inventory,
and cleanup capability as one complete pinned bundle while `CleanupPending`.
`RejectedPlanUnavailable` SHALL instead retain the immutable plan identity/hash, all
remaining verified plan bytes, the unavailability/corruption evidence, journal,
artifact inventory, quota, and cleanup capability; it SHALL NOT reconstruct missing or
untrusted bytes. Operation expiry or epoch sealing SHALL NOT release an adopted pin or
cleanup reference.

Before any mutation request bytes are sent, the caller SHALL atomically register a
bounded `DispatchPlanLease` in the protected coordination/plan authority, keyed by
repository and caller-known operation ID. Registration SHALL validate the authenticated
principal, operation-epoch token/claims, canonical request identity, exact readable
plan envelope/hash, pinned-byte/lease quota, and reserved capacity for authoritative
`NotRecorded` or terminal dispatch resolution. The lease SHALL bind plan ID/hash,
request hash, expected revision, claims hash, and epoch expiry and SHALL pin the exact
plan until receipt adoption, authoritative dispatch resolution, or epoch sealing. The
lease SHALL NOT imply transport acceptance; normal retention SHALL NOT expire its plan
while that operation identity remains admissible, and later quota exhaustion SHALL NOT
prevent a truthful dispatch-resolution record.

`PlanHash` SHALL be the SHA-256 digest of a versioned host-neutral
`CanonicalPlanPayload` serialization that omits the `PlanHash` field entirely rather
than encoding an empty, zero, or provisional value. The persisted envelope MAY carry
the claimed hash beside that payload. Every adapter SHALL use the same path/string
normalization, property ordering, explicit-null, default-value, and byte-encoding
rules and shared conformance vectors. Verification SHALL reserialize the payload with
the hash field omitted and compare the resulting digest in constant time.

#### Scenario: preview and apply use the same plan

- **WHEN** a prepared mutation is reviewed and then applied without drift
- **THEN** apply consumes the reviewed immutable plan
- **AND THEN** it does not recompute a semantically different mutation from the original command arguments

#### Scenario: a plan hash is computed without self-reference

- **WHEN** prepare computes or apply verifies `PlanHash`
- **THEN** the canonicalizer omits the `PlanHash` field from the hashed payload
- **AND THEN** CLI, MCP, in-process, and daemon adapters produce the same SHA-256 digest for the same semantic plan
- **AND THEN** changing any included plan field changes verification, while changing only a claimed `PlanHash` cannot alter the bytes being hashed

#### Scenario: review and apply use separate processes

- **WHEN** a caller reviews a prepared plan, exits, and later applies it through a new CLI, MCP, or daemon process
- **THEN** apply loads the exact persisted plan bytes whose repository ID, plan ID, and plan hash were reviewed
- **AND THEN** a missing, expired, schema-incompatible, or hash-mismatched plan is rejected before any repository write
- **AND THEN** repository-controlled files cannot replace the durable plan-store record

#### Scenario: retention elapses while recovery still references a plan

- **WHEN** normal plan-retention time elapses while an associated operation is `Applying`, `OwnerQuiescencePending`, `Reconciling`, `Recovering`, or `RecoveryRequired`
- **THEN** garbage collection retains the exact plan bytes and their journal, backup, and recovery references as one complete bundle
- **AND THEN** it releases them only after verified terminal cleanup or explicit recovery acknowledgement

#### Scenario: an operation expires after staging but before its first effect

- **WHEN** an accepted operation has journalled staging artifacts, reaches terminal `Expired` without a repository effect, and still reports `CleanupPending`
- **THEN** operation expiry and later epoch sealing retain the adopted plan pin plus journal, artifact inventory, quota, and cleanup capability as one referentially complete bundle
- **AND THEN** the pin is released only after the pre-effect activity fence drains and `CleanupVerified` proves the protected operation root empty

#### Scenario: a reviewed plan is leased before dispatch

- **WHEN** a caller has constructed its operation ID and canonical apply request but has not sent request bytes
- **THEN** it registers a quota-bounded `DispatchPlanLease` that validates and pins the exact plan under the repository/operation/request identity through epoch sealing
- **AND THEN** a durable receipt atomically adopts the pin, while status resolution converts it to `NotRecorded` or terminal `RejectedPlanUnavailable`
- **AND THEN** inability to validate or reserve the lease is reported before dispatch and no mutation request bytes are sent
- **AND THEN** proven `NotDispatched` may release the lease only under the intake fence after proving no intake attempt/receipt exists, while a caller crash leaves a quota-counted lease until epoch sealing

#### Scenario: a path escapes its assigned root

- **WHEN** a source/destination falls outside the canonical repository root, or a staging/backup/control path falls outside its coordination-domain-protected control root or overlaps the repository
- **THEN** preparation or preflight rejects the plan before any repository write

#### Scenario: staging is interrupted before repository effects

- **WHEN** apply is cancelled, crashes, or finds stale input while creating replacement content
- **THEN** a durable journal already identifies every staging intent and completed artifact
- **AND THEN** all staging and backup paths are under an operation-scoped coordination-domain-protected root outside the repository
- **AND THEN** restart/status cleanup is idempotent, performs no repository write, and retains the plan until artifact cleanup is verified
- **AND THEN** inability to create a safe outside-repository location, including a same-volume location when required, blocks unattended apply

#### Scenario: a path component changes after preflight

- **WHEN** a validated path component is replaced by a symlink, junction, mount point, reparse point, or different file identity before a create, open, stage, replace, move, delete, backup, or restore
- **THEN** the operation revalidates containment and identity through handle-relative no-follow or equivalent platform-safe operations at that side-effect boundary
- **AND THEN** it performs no write through the changed component
- **AND THEN** unsupported platform/filesystem combinations block unattended apply instead of relying on string-only or check-then-open validation
- **AND THEN** a race detected after earlier side effects enters verified rollback and terminates as `RolledBack` or `RecoveryRequired`

#### Scenario: an external process edits after validation

- **WHEN** an editor, build, script, or other non-cooperating process could change a reviewed dependency, affected object, or directory entry between validation and a filesystem effect
- **THEN** apply acquires an OS-enforced guard set or uses an atomic conditional effect that couples the reviewed identity/content/metadata/version tokens to that effect
- **AND THEN** it holds or revalidates that protection through journal intent, guarded apply or rollback, final verification, and durable outcome
- **AND THEN** an advisory lock, ordinary check-then-rename, or unconditional atomic replacement is insufficient
- **AND THEN** unsupported platform/filesystem combinations block unattended apply before the first effect
- **AND THEN** a lost guard or mismatch after an earlier effect stops the plan and terminates through guarded rollback as `RolledBack` or `RecoveryRequired`

#### Scenario: an affected file has hard-link aliases

- **WHEN** preparation or side-effect-time revalidation finds a link count greater than one, cannot inspect stable file identity/link count, or observes that either value changed after review
- **THEN** the plan declares `Reject`, `Preserve`, or `BreakByReplacement` hard-link handling and exposes its effects for review
- **AND THEN** `Preserve` is allowed only when every affected alias and shared-inode effect is identified and contained
- **AND THEN** `BreakByReplacement` changes only the declared directory entry and performs no in-place content, permission, or metadata mutation of the shared inode
- **AND THEN** unattended apply is blocked when an alias or effect is unknown, outside the verified boundary, unsupported, or different from the reviewed plan

#### Scenario: a changed file has no exact preservation policy

- **WHEN** the implementation cannot determine how to preserve or intentionally change encoding, BOM, newline, permissions, or symlink behaviour
- **THEN** preparation reports the limitation and blocks unattended apply

### Requirement: mutation coordination spans every writable principal

Canonical repository identity SHALL be stable across OS principals and hosts. One
OS- or service-protected registry outside repository control SHALL bind the checkout
filesystem identity, Git
common-directory identity, and a non-reused coordination nonce, and SHALL record the
coordination mode, declared owner or authority, active recovery-barrier summary, and
identity-retirement marker. A per-user store SHALL NOT shadow that mapping. Every
would-be mutation principal and host SHALL resolve the same registry before intake. A
machine-local registry SHALL qualify only when filesystem/ownership proof excludes
writes from every other host; shared or network storage SHALL require an authority
spanning every writer. Every
apply-capable plan SHALL name a coordination domain and
prove one of two modes. `ExclusivePrincipal` SHALL prove through OS
ACL/mode/ownership metadata that no other non-administrative principal or host in the
declared threat model can write or replace the repository root, Git common directory,
or ancestor entries capable of replacing either. Trusted OS administrators SHALL use
only the explicit transfer path. Its full ledger and payloads MAY remain
principal-private, but
admission, ownership, active recovery-barrier status, and transfer SHALL remain in
the cross-principal registry. `SharedAuthority` SHALL use one OS- or service-enforced
authority outside repository control for intake fencing, operation state, transaction
reservation, fencing epochs, recovery barriers, operation-epoch issuance/retirement,
retained outcomes, the retirement watermark/accumulator, and pinned records across
every authorized principal and host adapter. Protected operation payloads and
recovery artifacts MAY remain encrypted or access-controlled within that authority.
Normal retention SHALL NOT delete the repository mapping or an active barrier.

First-time repository enrollment SHALL be an explicit, separately authorized
registry operation and SHALL NOT occur implicitly during prepare or apply. The
registry SHALL maintain an OS/service-protected `AuthorityId`, continuity generation,
monotonically increasing `RepositorySequence`, and append-only enrollment/retirement
audit root. Startup and restore SHALL prove the generation, sequence high-water mark,
and audit root continue from the last protected checkpoint. An absent, rolled-back,
unreachable, or inconsistent registry SHALL NOT treat itself as empty.

Authority provisioning SHALL be a separate administrator operation in a demonstrably
fresh OS/service namespace backed by a non-rollback continuity anchor; it SHALL NOT
be repository enrollment. A platform unable to prove fresh provisioning or later
continuity SHALL NOT qualify as a mutation coordination authority.

The authority SHALL bound, both authority-wide and per authenticated principal, live
repository count/bytes, concurrent enrollment/retirement work, enrollment rate, and
retired-binding index count/bytes. Before advancing `RepositorySequence`, enrollment
SHALL atomically reserve capacity for the live mapping, audit update, and one eventual
fixed-size retired-binding entry so retirement of an admitted repository cannot later
fail for quota. Exhaustion SHALL return typed `RepositoryCapacityExceeded` without a
mapping or implicit authority expansion; existing mutation status, cleanup, recovery,
transfer, and retirement SHALL remain available. Capacity changes SHALL be explicit,
authenticated, audited administrator operations.

Retirement SHALL fold the bulky final marker and audit detail into a fixed-size
cumulative authenticated `RetiredRepositoryAccumulator` and retain a fixed-size protected
`RetiredBindingIndex` entry containing domain-separated keyed digests of repository ID and canonical
physical binding, final generations/watermarks, and the final audit root. The monotonic
repository-sequence high-water mark and index SHALL never roll back, delete an entry,
or produce a false negative within an authority lifetime; a digest collision or
ambiguous proof SHALL conservatively reject enrollment. Bounded recent audit proofs MAY
remain hot; older bulky detail MAY move to a separately quota-bound audit archive or be
deleted from the coordination store only after the accumulator and index commit. Once
all reserved entries are consumed, new enrollment SHALL fail rather than grow the
coordination store or reuse a retired identity.

Enrollment SHALL open verified handles for the checkout root and Git common directory
without evaluating repository code, derive their stable physical identity,
authenticate an actor authorized for the declared coordination mode and writer set,
and acquire one authority-enforced enrollment fence keyed by that identity. In one
transaction it SHALL consult live and retired bindings, prove authoritative absence
in the continuity-complete registry, advance `RepositorySequence`, and persist the
new `RepositoryId = (AuthorityId, RepositorySequence)`, non-reused coordination nonce,
physical binding, owner/authority proof, initial no-barrier state, and audit entry
before returning. A crash MAY consume a sequence number but SHALL NOT expose a partial
mapping or permit reuse. Concurrent enrollment SHALL resolve to the same committed
identity.

A healthy registry with no binding SHALL return typed `RepositoryNotEnrolled` to
mutation and MAY offer the explicit enrollment operation. A live binding SHALL be
reused rather than replaced. Retired/conflicting bindings, missing authority state,
failed continuity proof, repository-supplied identity markers, or handle/path
replacement SHALL return `RejectedUnsafeCoordination` and SHALL NOT mint an identity.
A replacement checkout SHALL require a distinct proven physical identity and receive
a later sequence; ownership changes SHALL use the explicit transfer path.

If neither proof is available, mutation preparation SHALL NOT return an apply-capable
plan and an accepted apply SHALL terminate as no-effect
`RejectedUnsafeCoordination`. Apply SHALL revalidate the writer set and authority
before the first repository effect and at every effect boundary. Coordination drift
after an effect SHALL enter verified rollback or `RecoveryRequired` rather than a
no-effect rejection. `RecoveryRequired` SHALL be committed to the coordination
authority and cross-principal registry before releasing the repository reservation,
and every later apply from any principal SHALL consult that barrier. Ownership
transfer SHALL be explicit, authority-fenced, and preserve `RepositoryId` for the same
physical checkout; it SHALL NOT silently create a new user-private mutation domain or
retire the identity. The authority SHALL atomically enter `TransferPending`, block new
operation epochs, apply-capable preparation, dispatch-lease registration, intake,
replay promotion, and concurrent ownership changes, and seal open operation epochs while leaving status, cancellation,
cleanup, and recovery available.

Before transfer commits, the authority SHALL use the existing repository reservation
and coordination generation to wait for every live owner or reconcile it after lease
expiry only after proven reservation release plus a closed/drained effect fence or
verified worker termination and write-capability closure. It SHALL prove no dispatch
plan lease, intake attempt, or receipt promotion is in flight, no transaction
reservation, live owner, or open/unreconciled effect fence remains, every
nonterminal operation is terminal, every recovery barrier is recovered or explicitly
acknowledged, and every `NotRecorded` replay authorization is expired. Retained
outcomes, watermark/accumulator, pinned terminal cleanup records, plans, and audit
history SHALL carry forward with access controls rewrapped as needed. One transaction
SHALL then increment `CoordinationGeneration`, install the new owner/authority and
writer set, rotate admission/fencing credentials, and release `TransferPending`. Old
tokens, writer-set proofs, leases, and prepared plans SHALL fail generation
revalidation. A crash SHALL expose old ownership, a durable transfer-pending fence, or
the completely committed new ownership, never two writable domains. Physical checkout
replacement SHALL use quiescent retirement plus enrollment rather than ownership
transfer.

#### Scenario: a shared writable checkout has no coordination authority

- **WHEN** more than one OS principal or host can write or replace the checkout or Git common directory and no shared authority covers all of them
- **THEN** inspection remains available but mutation preparation does not return an apply-capable plan
- **AND THEN** an accepted apply records `RejectedUnsafeCoordination` and performs no repository effect

#### Scenario: a new checkout is explicitly enrolled

- **WHEN** an authorized actor requests enrollment for an unenrolled checkout through a healthy continuity-complete registry
- **THEN** the authority verifies handle-based checkout/Git-common-dir identity and the requested coordination mode under the enrollment fence
- **AND THEN** it proves no live or retired binding exists, advances the durable repository sequence, and atomically persists the complete mapping and audit entry before returning
- **AND THEN** every concurrent enrollment receives that same repository identity and no mutation path performs enrollment implicitly

#### Scenario: repository enrollment capacity is exhausted

- **WHEN** an authority-wide or per-principal live-mapping, rate, byte, or reserved retired-binding limit is reached
- **THEN** enrollment returns `RepositoryCapacityExceeded` before advancing the repository sequence or persisting a mapping
- **AND THEN** it does not bypass the limit by reprovisioning the authority or omitting the eventual retirement reservation
- **AND THEN** existing status, cleanup, recovery, transfer, and retirement remain available

#### Scenario: an unenrolled checkout reaches mutation

- **WHEN** prepare or apply resolves a healthy registry with no binding for the verified physical identity
- **THEN** it returns `RepositoryNotEnrolled` with explicit enrollment guidance and performs no repository effect
- **AND THEN** repository-controlled files cannot supply identity or prove authoritative absence

#### Scenario: registry continuity cannot be proven

- **WHEN** the authority store or protected checkpoint is absent, rolled back, unreachable, inconsistent, or disagrees with the enrollment/retirement audit root
- **THEN** enrollment and mutation return `RejectedUnsafeCoordination`
- **AND THEN** no new authority generation, repository sequence, identity, or empty barrier state is inferred, and repository enrollment cannot reprovision the authority

#### Scenario: enrollment crashes or races

- **WHEN** two actors enroll the same verified physical identity concurrently or the authority crashes at an enrollment transaction boundary
- **THEN** recovery exposes either no mapping or one fully initialized mapping
- **AND THEN** all successful callers receive the same identity; unused sequence gaps are allowed but a sequence, retired binding, or repository ID is never reused

#### Scenario: another principal targets a repository awaiting recovery

- **WHEN** one principal leaves a shared-authority repository in `RecoveryRequired` and another principal submits an apply
- **THEN** both resolve the same canonical repository ID and coordination authority
- **AND THEN** the second apply observes the durable barrier and is rejected before any repository effect

#### Scenario: mutation ownership changes during apply

- **WHEN** the proven writer set, coordination authority, or ownership identity changes after plan review
- **THEN** detection before the first effect records `RejectedUnsafeCoordination` with no repository effect
- **AND THEN** detection after an effect stops remaining work and reaches verified `RolledBack` or `RecoveryRequired`

#### Scenario: repository mutation ownership is transferred

- **WHEN** an administrator transfers a checkout to another mutation principal
- **THEN** the authority enters `TransferPending`, blocks new mutation admission, and preserves the existing repository ID
- **AND THEN** it waits for or reconciles every active operation under the existing reservation/generation and requires verified recovery or explicit acknowledgement of every barrier
- **AND THEN** it proves no dispatch plan lease, intake lease, receipt promotion, reservation, live owner, nonterminal operation, or replay authorization remains before changing ownership
- **AND THEN** it carries forward retained/pinned records and epoch state, increments `CoordinationGeneration`, rotates credentials, and only then releases the transfer fence
- **AND THEN** an old owner cannot mutate with its prior token, plan proof, lease, or fencing generation

#### Scenario: ownership transfer races an active apply

- **WHEN** transfer begins while an operation is `Applying`, `Recovering`, `OwnerQuiescencePending`, `Reconciling`, or otherwise nonterminal
- **THEN** no new mutation intake is admitted and transfer does not change the ownership domain or repository identity yet
- **AND THEN** the active owner completes under the old reservation, or an expired owner is fenced and reconciled under that same reservation only after its effect fence is closed/drained or its worker termination and write-capability closure are verified
- **AND THEN** transfer remains pending if the operation or reservation cannot be proven quiescent

### Requirement: apply operations have caller-known durable identity

Before apply dispatch, the caller SHALL obtain a bounded repository-scoped
`OperationEpochToken` from the coordination authority and construct its operation ID
from the authenticated monotonically increasing epoch plus a caller-generated nonce.
The operation epoch SHALL be distinct from the owner fencing epoch and from the
one-use replay authorization. The token SHALL be repository/principal bound,
schema-versioned, count/byte/time-limited, and carry `NotAfter`; the authority SHALL
validate it before accepting an unknown operation ID.
Every apply request SHALL include the token, that caller-known structured operation
ID, and canonical request hash before dispatch. The request hash SHALL be the SHA-256
digest of a versioned UTF-8 canonical serialization containing every semantic
work-defining field, including repository ID, plan ID/hash, operation ID,
`OperationEpochClaimsHash`, and expected revision. `OperationEpochClaimsHash` SHALL
cover the token's canonical public repository/principal, epoch, schema, limit, and
`NotAfter` claims, but not its signature or bearer proof. The request hash SHALL omit
`RequestHash` itself, the separate one-use `NotRecorded` replay epoch, transport
attempt/correlation metadata, cancellation state, and authentication proof. All
adapters SHALL use the same canonicalizer and test vectors rather than hashing
transport-specific JSON.
Before sending request bytes, every adapter SHALL obtain the matching
`DispatchPlanLease`; the receiver SHALL reject an unknown operation ID without a valid
matching lease. Receipt creation SHALL atomically adopt the lease's plan pin.
Canonical repository ID plus operation ID SHALL be the composite durable identity
for intake, idempotency, status, recovery, acknowledgement, and retention; operation
IDs may repeat across repositories. Every operation lookup or mutation SHALL carry
both values, but the pair SHALL be an address and SHALL NOT confer authority.

Every lifecycle request (`ResolveDispatch`, `Replay`, `ReadStatus`, `Cancel`, `Resume`,
`Rollback`, `AcknowledgeRecovery`, `Cleanup`, or `Reconcile`) SHALL carry an
`AuthenticatedPrincipalId` derived from a protected host/transport context and declare
one action. The authority SHALL evaluate current policy directly or validate a
short-lived principal-, audience-, repository-, operation-scope-, action-,
`CoordinationGeneration`-, authorization-generation-, and expiry-bound
`LifecycleAuthorization`. Roles, principal identity, or self-asserted authorization
data supplied by repository content or an ordinary request field SHALL NOT grant authority. Every
adapter SHALL reauthorize before returning existence-specific data, acquiring a fence,
issuing replay, releasing artifacts, or changing ledger/barrier state. Transfer, role
revocation, or authorization-generation rotation SHALL invalidate stale proof.

`ResolveDispatch` and `Replay` SHALL require the matching submitting principal and
original request/token proof with current mutation permission, or the narrowly scoped
authority reconciler. `ReadStatus` SHALL require a currently permitted submitter with
`ReadOwnOperation`, `OperationReader`, `RecoveryOperator`, or
`RepositoryLifecycleAdministrator`. `Cancel` SHALL require the matching submitter with
`CancelOwnOperation`, `OperationCanceller`, or the lifecycle administrator. `Resume`
and `Rollback` SHALL require `RecoveryOperator` or the lifecycle administrator.
`AcknowledgeRecovery` SHALL require the distinct `RecoveryAcknowledger` role or the
lifecycle administrator plus deliberate invocation and a reason; ordinary mutation or
recovery permission SHALL be insufficient. `Cleanup` SHALL require recovery/lifecycle
administration or the narrowly scoped cleanup service identity. `Reconcile` SHALL be
limited to the authenticated coordination-authority service identity and deterministic
fenced reconciliation.

An authorized `ReadStatus` request MAY cause the authority to schedule required
reconciliation or cleanup, but the caller SHALL NOT inherit those service roles. The
internal action SHALL run under its constrained service identity with authority-derived
inputs for that operation, and the result SHALL be reauthorized for `ReadStatus` before
disclosure. An unauthorized lookup SHALL NOT trigger reconciliation or cleanup as a
confused-deputy side effect.

An unauthorized lifecycle request SHALL return typed `OperationAccessDenied` before
disclosing whether the operation exists or returning its state, hashes, paths, actor,
or evidence, and SHALL NOT mutate its ledger, pins, artifacts, reservations, or barrier.
A principal otherwise allowed to propose mutation MAY receive only generic
`RepositoryRecoveryBlocked` state needed to reject new work, without the protected
operation ID or evidence. Successful lifecycle changes SHALL record the authenticated
actor, effective role/action, and authorization generation in bounded audit evidence;
denial telemetry SHALL be rate-limited and compacted outside the operation ledger.

Before accepted work can enter an execution queue, the receiver SHALL
acquire a cross-process, store-enforced intake fence keyed by repository and operation
ID; validate the token, claims hash, epoch/ID match, and authenticated-principal
binding; validate the matching dispatch lease and exact plan availability; reserve
bounded worst-case receipt/ledger/outcome/recovery-metadata capacity, including the
recovery-attempt structures and next truthful transition defined below;
and durably record a `Received` receipt containing the repository ID, operation ID,
request hash, canonical request, submitting principal, and authorization generation. The operation ledger SHALL then be recorded
before repository side effects begin. A `Received` receipt without an operation
record SHALL be reconciled on startup and status lookup. Token validation, the
epoch-open/quota check, capacity reservation, and receipt creation SHALL be atomic
with respect to epoch sealing and concurrent admission.

Every terminal transition, including durable expiry, SHALL atomically retain a
compact outcome record keyed by canonical repository ID and operation ID for a
declared minimum replay window. That record SHALL preserve request and plan hashes,
terminal state, canonical outcome, completion time, final evidence digest, and
required cleanup/recovery summary. The intake fence SHALL consult active and retained
records before creating `Received`, `NotRecorded`, or `RejectedPlanUnavailable`.

The authority SHALL bound per-repository and per-principal dispatch-plan leases and
pinned plan bytes, open operations,
simultaneously open/unretired epochs, accepted operation count/bytes per epoch,
epoch issuance rate, replay-window duration, terminal-record bytes, and pinned
recovery/cleanup exceptions. It SHALL seal an epoch at its token's `NotAfter` or
earlier quota, accept no unknown operation ID or replay authorization in a sealed
epoch, accept no new dispatch lease, and return typed `CoordinationCapacityExceeded` before dispatch/intake rather than mint
unlimited epochs when admission limits are exhausted. Existing live and retained
records SHALL remain queryable after sealing. Sealing SHALL atomically transition
every outstanding `NotRecorded` record to terminal `Expired` and invalidate its
replay authorization; after intake attempts are quiescent it SHALL expire unadopted
dispatch leases and release their plan pins while already accepted operations continue. Status and recovery
SHALL remain available while mutation admission is blocked. Quotas SHALL NOT prevent an accepted
operation from recording any truthful transition, terminal outcome, cleanup state, or
recovery result within its declared resource budget.

Every accepted mutation SHALL reserve capacity for a fixed-size authenticated
`RecoveryAttemptAccumulator`, monotonic non-reused `RecoveryAttemptSequence` and
`CompactedThroughRecoveryAttempt`, a bounded hot-attempt count/byte window, one
maximum-size in-flight recovery journal segment, one plan-bounded
`CurrentRecoveryCheckpoint`, one plan-bounded active recovery-artifact set, and the next truthful recovery transition. The checkpoint
SHALL contain the latest verified journal frontier; existence, type, physical identity,
hash, and required metadata for every plan-affected path; unresolved effect intents;
plan/backup references; remaining deterministic resume/rollback actions; active
recovery-artifact inventory; and the last outcome classification. Arbitrary diagnostic/log fields SHALL have count/byte caps and
retain a digest when detail is omitted. The original plan, base journal, backups, and
unresolved current-attempt evidence SHALL remain pinned.

Only one recovery attempt SHALL be active for an operation. Before a later attempt may
perform a repository effect, every fully quiesced attempt outside the hot window SHALL
be atomically folded into `RecoveryAttemptAccumulator`, the compaction watermark SHALL
advance, and its pre-reserved slot SHALL become reusable. An owner, effect fence,
one-use intent, observed effect result, or checkpoint that is still unresolved SHALL
NOT be compacted. If slot reuse, authority continuity, or compaction cannot be proven,
the operation SHALL remain `RecoveryRequired` and the incoming request SHALL return
typed `RecoveryAttemptUnavailable` before an effect. An inconclusive attempt SHALL use
its reserved space to atomically commit a bounded immutable summary, replace
`CurrentRecoveryCheckpoint`, and return to `RecoveryRequired`; old detail MAY be
deleted or moved to a separately quota-bound archive, and superseded attempt artifacts
MAY be removed, only after the accumulator, replacement checkpoint, and active-artifact
set commit. Startup and restore SHALL prove the sequence, watermark, accumulator,
checkpoint, and active-artifact inventory agree or retain the recovery barrier and
admit no recovery effect.

After the replay window, the authority SHALL atomically fold eligible terminal
digests into a cumulative `RetiredOperationAccumulator`, advance a monotonic
`RetiredThroughEpoch` watermark, and only then remove individual outcome records.
Nonterminal, `RecoveryRequired`, and cleanup-pending operations SHALL remain in a
bounded pinned-exception index; reaching its cap SHALL block new mutation admission.
Lookup SHALL resolve pinned/live records before the watermark. An otherwise unknown
operation at or below the watermark SHALL return `OperationEpochRetired` and SHALL
NOT create `NotRecorded`, authorize replay, or execute. The watermark and accumulator
SHALL never move backward. Repository/epoch identifiers SHALL never be reused.

Repository-identity retirement SHALL be distinct from ownership transfer and SHALL
first enter a durable `RetirementPending` fence that blocks new epochs, apply-capable
preparation, dispatch-lease registration, intake, replay, and transfer while preserving status, cancellation,
cleanup, acknowledgement, and recovery access. Under the existing repository identity
and reservation, retirement SHALL seal all epochs and wait for or reconcile every
dispatch plan lease, intake attempt, receipt promotion, live owner, reservation,
effect fence, and nonterminal operation. `OwnerQuiescencePending` SHALL block
retirement until the stale worker is proven unable to execute.
Every `RecoveryRequired` record SHALL reach verified recovery or terminal
`RecoveryAcknowledged`; every cleanup-pending record SHALL verify cleanup; no
plan/recovery/cleanup record SHALL remain pinned; and every retained terminal replay
window SHALL elapse and compact so `RetiredThroughEpoch` covers every issued epoch.

Only after those preconditions are durably proven SHALL one transaction replace the
live mapping with its reserved fixed-size `RetiredBindingIndex` entry and atomically
fold the final watermark, operation accumulator, physical binding digest, maximum
repository/operation generation, retirement actor/reason/time, and audit root including
acknowledgement summaries into `RetiredRepositoryAccumulator`. After
commit no operation may remain that requires status or recovery, and every later
request carrying the retired repository ID SHALL be rejected before intake. If
quiescence or cleanup cannot be proven, retirement SHALL remain pending or fail
without removing status/recovery access.

#### Scenario: a caller constructs the canonical request hash

- **WHEN** a caller prepares an apply request for dispatch
- **THEN** it serializes the semantic request with the shared schema version, path/string normalization, property ordering, explicit-null, and default-value rules
- **AND THEN** `OperationEpochClaimsHash` covers the token's canonical public claims while its signature/bearer proof, `RequestHash`, replay epoch, transport metadata, cancellation state, and other authentication proof are absent from the hashed bytes
- **AND THEN** CLI, MCP, in-process, and daemon adapters produce the same SHA-256 digest for the same semantic request

#### Scenario: a caller creates an operation ID before dispatch

- **WHEN** a caller is ready to submit an apply request
- **THEN** it obtains the current authenticated operation-epoch token and combines its epoch with a caller-generated nonce
- **AND THEN** the complete structured operation ID is known before dispatch and remains available if the apply response is lost
- **AND THEN** the separate `NotRecorded` replay epoch cannot extend the token's `NotAfter`

#### Scenario: terminal history reaches its retention limit

- **WHEN** a sealed operation epoch has passed its replay window and eligible terminal records are compacted
- **THEN** their digests are folded into the cumulative accumulator and `RetiredThroughEpoch` advances before individual records are removed
- **AND THEN** a delayed request whose individual record was removed returns `OperationEpochRetired` without execution or replay authorization
- **AND THEN** pinned recovery/cleanup records remain individually addressable and count against a bounded admission quota

#### Scenario: an unknown operation arrives after its epoch seals

- **WHEN** no live or retained record exists for an operation ID and its authenticated operation epoch is sealed but not yet retired
- **THEN** the authority returns `OperationEpochSealed`
- **AND THEN** it does not create `Received` or `NotRecorded`, honor an expired replay authorization, or execute repository work
- **AND THEN** any `NotRecorded` record that existed when sealing began is terminal `Expired`, not replayable
- **AND THEN** the caller uses a newly admitted current-epoch operation ID for any new attempt

#### Scenario: a client exhausts operation admission

- **WHEN** a principal or repository reaches its open-operation, open/unretired-epoch, issuance-rate, accepted-count/byte, terminal-byte, or pinned-exception limit
- **THEN** the authority returns `CoordinationCapacityExceeded` before accepting another mutation
- **AND THEN** it does not evade the limit by minting unlimited epochs
- **AND THEN** status, cleanup, acknowledgement, and recovery remain available to reduce retained state, and previously accepted operations retain their reserved capacity to record truthful outcomes

#### Scenario: a repository identity is explicitly retired

- **WHEN** an administrator retires a canonical repository identity
- **THEN** the authority enters `RetirementPending`, blocks new mutation admission/transfer, and keeps status, cleanup, acknowledgement, and recovery available
- **AND THEN** it seals epochs and proves every dispatch-lease/intake/reservation/owner/nonterminal state is quiescent, every recovery or cleanup record is resolved, no record remains pinned, and every retained outcome is compacted through the final issued epoch
- **AND THEN** it consumes the enrollment-reserved fixed-size retired-binding entry and atomically folds the final marker into the authenticated retired-repository accumulator
- **AND THEN** only after no operation can require status or recovery does every later request carrying the never-reused repository ID reject before intake

#### Scenario: retired binding detail is compacted

- **WHEN** a retired repository's bulky marker or old audit detail leaves its bounded hot-retention window
- **THEN** the authority retains its fixed-size keyed repository/binding digest entry and cumulative authenticated accumulator root before deleting bulky detail
- **AND THEN** later enrollment of that physical binding is rejected without reviving or reusing the retired repository ID
- **AND THEN** collision, missing proof, index saturation, or continuity failure rejects new enrollment rather than create a false absence

#### Scenario: retirement encounters pinned recovery work

- **WHEN** retirement finds a nonterminal, `RecoveryRequired`, cleanup-pending, retained replay-window, or otherwise pinned record
- **THEN** it does not commit the retired-binding index entry or remove status/recovery access
- **AND THEN** it waits for verified terminal cleanup/recovery/compaction or fails with the blocking identities and required action

#### Scenario: the first response is lost

- **WHEN** an apply request may have reached the execution host but its response is unavailable
- **THEN** the caller still knows the operation ID
- **AND THEN** it can query operation status without generating a second apply operation

#### Scenario: execution dies before operation-ledger creation

- **WHEN** dispatch is indeterminate, the operation epoch remains open, and status lookup finds neither a durable intake receipt nor an operation record
- **THEN** status lookup validates the original operation-epoch token and an `OperationRequestIdentity` containing request hash, plan ID/hash, expected revision, and operation-epoch claims hash
- **AND THEN** it validates the matching `DispatchPlanLease`, acquires the same operation-intake fence, and proves no receiver for that operation ID remains in flight
- **AND THEN** when the exact plan remains readable and verified, the epoch-open check and durable `NotRecorded` creation are atomic with sealing, the record adopts that plan pin, and it returns a replay epoch
- **AND THEN** a delayed original attempt without that epoch is rejected before execution
- **AND THEN** the caller may explicitly resubmit the exact request before the operation epoch seals, using the same operation ID, request hash, and replay epoch to transition `NotRecorded` to `Received` exactly once
- **AND THEN** this path performs no automatic retry and no in-process fallback

#### Scenario: a potentially dispatched plan becomes unavailable

- **WHEN** dispatch is indeterminate, the operation epoch remains open, no receipt or operation exists, no receiver remains in flight, and the dispatch lease is missing/expired or its exact plan is missing, expired, unreadable, schema-incompatible, or hash-mismatched
- **THEN** status uses the same intake fence to atomically record terminal no-effect `RejectedPlanUnavailable` with the operation/request identity, reason, and evidence
- **AND THEN** it returns no replay authorization, a delayed original receiver is permanently rejected, and later plan reappearance cannot revive the operation
- **AND THEN** only after observing that terminal outcome may the caller re-prepare current inputs and use a new current-epoch operation ID

#### Scenario: an identical operation is repeated

- **WHEN** the matching authenticated submitting principal, currently authorized for replay, submits the same operation ID with the same canonical plan, request hash, and expected revision while its live or retained outcome record exists
- **THEN** the existing intake receipt, operation state, or terminal result is returned
- **AND THEN** repository side effects are not applied again

#### Scenario: an operation ID is reused for different work

- **WHEN** the matching authenticated submitting principal, currently authorized for replay, submits a live or retained operation ID with a different plan, payload, or expected revision
- **THEN** the request returns `OperationConflict`
- **AND THEN** no repository path is written
- **AND THEN** the canonical receipt, ledger state/history, owner, reservations, plan pins, and outcome remain unchanged, so an exact replay continues to observe the original operation

#### Scenario: an operation is repeated after its epoch is compacted

- **WHEN** a request carries an operation ID whose live/retained record is absent and whose epoch is at or below `RetiredThroughEpoch`
- **THEN** lookup returns `OperationEpochRetired`
- **AND THEN** it does not create `NotRecorded`, disclose a deleted outcome, distinguish payload hashes, authorize replay, or execute repository work

#### Scenario: two repositories use the same structured operation value

- **WHEN** two canonical repositories submit different work under the same epoch/nonce value authenticated by their respective repository-scoped tokens
- **THEN** their composite repository/operation identities address independent receipts, ledgers, status, recovery, and retention
- **AND THEN** status, resume, rollback, and acknowledgement require the canonical repository ID plus current action-specific authorization and cannot observe or mutate the other repository's operation

#### Scenario: another principal learns an operation identity

- **WHEN** a principal in the same shared authority knows another principal's repository and operation IDs but lacks the action-specific lifecycle role
- **THEN** identical or conflicting resubmission, status, dispatch resolution, replay, cancellation, resume, rollback, acknowledgement, cleanup, and reconciliation each return `OperationAccessDenied` without operation-specific existence or evidence disclosure
- **AND THEN** no ledger state, owner, fence, reservation, pin, artifact, replay permission, or recovery barrier changes
- **AND THEN** if the caller is otherwise allowed to propose mutation, it receives only generic `RepositoryRecoveryBlocked` when a barrier must prevent new work

#### Scenario: lifecycle authorization becomes stale

- **WHEN** repository ownership transfers, a role is revoked, or the coordination or authorization generation advances after a lifecycle authorization was issued
- **THEN** every adapter rejects that stale authorization before lookup disclosure or mutation and requires a current action-scoped decision
- **AND THEN** an operation ID, prior endpoint authentication, submitting-principal record, or former role does not preserve access by itself

#### Scenario: a recovery operator attempts acknowledgement

- **WHEN** an authenticated `RecoveryOperator` without `RecoveryAcknowledger` or lifecycle-administrator authority attempts to acknowledge unresolved recovery
- **THEN** the authority returns `OperationAccessDenied`, discloses no additional recovery evidence, and leaves `RecoveryRequired` and every pinned artifact unchanged

### Requirement: reviewed mutations are failure-aware and recoverable

Apply SHALL verify every precondition before writes, durably journal staging intent,
stage replacement data outside the repository, durably journal side effects, apply
changes deterministically, verify postconditions, and attempt rollback after an
apply failure. Before final whole-plan preflight, each apply/recovery SHALL acquire
an exclusive OS-enforced transaction reservation keyed by canonical repository
identity and SHALL establish complete external-write protection by either acquiring
a deterministic guard set over every reviewed dependency, affected object, and
directory entry or recording immutable version tokens that every effect checks
atomically. It SHALL retain both boundaries through durable terminal completion or
explicit recovery handoff. Each `Applying` or `Recovering` record SHALL carry a
renewable owner lease and monotonically increasing fencing epoch. Before the first
repository effect, one store-enforced compare-and-swap SHALL either commit
`CancelledNoEffect` when cancellation is pending or durably append
`FirstEffectStarted` with the complete effect intent, expected intermediate state,
and guard/version tokens and make the fenced owner eligible to request exactly that
effect. It SHALL NOT be a reusable bearer permission to write. A mutation worker SHALL
have no bypassable direct repository-write authority: every effect SHALL be admitted
at the actual filesystem-operation boundary by an OS-enforced broker/fence that
atomically revalidates the reservation, epoch, lease, ledger state, one-use intent,
and guard/conditional tokens. Effect admission and closing/revoking that fence SHALL
be linearizable. Once an effect wins admission, the fence SHALL NOT be reported
quiescent until the conditional filesystem operation and its observed journal outcome
are complete. If the platform cannot provide such a broker, epoch takeover SHALL be
forbidden until verified worker termination has closed every repository-write handle.
After owner loss, the marker SHALL be treated as possibly effected until
reconciliation proves its observed outcome from durable evidence.
Every side effect and every expired-owner takeover SHALL validate the reservation,
external-write guards or conditional-effect tokens, current ledger state, epoch,
lease, expected intermediate repository state, prior effects, remaining plan
dependencies, platform file identity, and link count. An expired owner SHALL be
reconciled from durable evidence. The ledger SHALL retain a logically append-only
transition history: transition sequences SHALL never be reused or reordered, while
eligible fully quiesced recovery-attempt subhistory MAY be removed only through the
authenticated accumulator/watermark transaction above. Direct terminal states SHALL distinguish `Applied` and `RolledBack`;
verified post-recovery terminal states SHALL distinguish `RecoveredApplied` and
`RecoveredRolledBack`; `RecoveryAcknowledged` SHALL be an administrative terminal
state that makes no repository-cleanliness claim. `CancelledNoEffect`,
`RejectedStale`, `RejectedPlanUnavailable`,
`RejectedUnsafeCoordination`, and `Expired` SHALL
remain no-effect terminal outcomes. `RecoveryRequired` SHALL be a durable intervention state, while `Received`,
`NotRecorded`, `Prepared`, `Applying`, `Recovering`, `OwnerQuiescencePending`, and
`Reconciling` are nonterminal or dormant resolution states.

`OperationConflict` SHALL be a response scoped to a mismatched incoming attempt, not
an operation-ledger state or transition. Returning it SHALL NOT terminate or otherwise
mutate the canonical receipt, ledger history, ownership, reservations, pins, or
eventual outcome for `(RepositoryId, OperationId)`.

Every pre-effect control-root create/write/delete, staging or backup action, guard
acquisition/release, and final-preflight activity SHALL be admitted through one
operation-scoped `PreEffectActivityFence` bound to the owner and a monotonic control
generation. The worker SHALL have no bypassable direct authority over the protected
control root or guard manager. Cancellation or any no-effect resolution SHALL
atomically close new effect and pre-effect activity admission and advance the control
generation. Activity admitted first MAY drain, but a stale generation SHALL NOT create
or journal another artifact or acquire a guard afterward.

A terminal no-effect outcome MAY report `CleanupPending`, but cleanup SHALL NOT
inventory/remove artifacts or append `CleanupVerified` until the pre-effect fence is
closed and drained, every guard is released, and the old worker cannot perform another
control-plane write. Unreachable activity state SHALL fail closed as pending. The exact
plan, artifact inventory, journal, quota reservation, and cleanup capability SHALL stay
pinned until verified cleanup, so a terminal outcome never makes an unquiesced control
root appear clean.

If `Received` exists but the exact leased plan becomes unavailable or fails envelope,
schema, or hash verification before `FirstEffectStarted`, the operation SHALL
atomically terminate as no-effect `RejectedPlanUnavailable`; it SHALL NOT reconstruct
the plan or remain indefinitely nonterminal.

An apply reconciler MAY transition from `Reconciling` to `CancelledNoEffect`,
`RejectedStale`, `RejectedPlanUnavailable`, `RejectedUnsafeCoordination`, or `Expired` only while holding the
fenced repository reservation and after verifying an intact ledger/journal containing
no `FirstEffectStarted` permit and proving no repository effect was possible. It SHALL
rerun final preflight before selecting an outcome whose inputs remain available; for
`RejectedPlanUnavailable` it SHALL instead record the verified lease/plan-store failure
and inability to reconstruct the plan. It SHALL NOT roll back unrelated external drift. If no-effect proof is absent or contradictory, it
SHALL resolve the possibly effected journal to `Applied` or `RolledBack`, or retain
evidence in `RecoveryRequired`.

#### Scenario: two apply plans target the same repository

- **WHEN** two apply or recovery requests for the same canonical repository overlap
- **THEN** only one acquires the exclusive transaction reservation and enters final whole-plan preflight
- **AND THEN** the other waits, cancels, or rejects before final validation and performs no repository side effect
- **AND THEN** the owner holds the reservation through every journalled effect and durable terminal result or explicit recovery handoff
- **AND THEN** each effect revalidates the expected intermediate state and every still-relevant reviewed dependency, so neither a queued plan nor external drift can be silently accepted

#### Scenario: an accepted apply is cancelled before its first repository effect

- **WHEN** an authenticated caller authorized for `Cancel` targets an operation with a durable `Received` receipt before `FirstEffectStarted`, including while awaiting the repository reservation, staging, acquiring external-write protection, or running final preflight
- **THEN** cancellation uses the same cross-process intake fence when receipt-to-ledger promotion is still pending, and one store-enforced compare-and-swap records `CancelledNoEffect`, closes effect and pre-effect activity admission, and advances the control generation under the same repository ID, operation ID, and request hash
- **AND THEN** neither receipt promotion nor the current owner can enqueue or start an effect, create or journal a control artifact, or acquire a guard after cancellation wins
- **AND THEN** the terminal record contains cancellation timing, journal sequence, proof that no first-effect permit exists, staged-artifact inventory, and cleanup status
- **AND THEN** startup and status lookup wait for admitted pre-effect activity to drain and every guard to release before cleaning outside-repository artifacts and appending `CleanupVerified`; the exact plan and cleanup capacity remain pinned until then

#### Scenario: cancellation cleanup races an admitted staging write

- **WHEN** a staging worker is paused after pre-effect activity admission and cancellation wins before its control-root write completes
- **THEN** the cancellation result reports `CleanupPending` and cleanup does not inventory, remove, or verify the control root while that admission remains in flight
- **AND THEN** after the admitted activity drains, the stale control generation prevents the worker from creating or journalling any later artifact
- **AND THEN** cleanup appends `CleanupVerified` only after an exclusive rescan proves the protected operation root empty and all guards released

#### Scenario: cancellation races the first repository effect

- **WHEN** cancellation and the first guarded repository effect become eligible concurrently
- **THEN** one durable compare-and-swap either commits `CancelledNoEffect` or appends `FirstEffectStarted` with the complete first-effect intent and guard/version evidence, making the current fenced owner eligible to request exactly that effect through the non-bypassable effect broker
- **AND THEN** if cancellation wins, no repository effect can start
- **AND THEN** if the effect permit wins, broker admission revalidates current authority at the actual conditional filesystem boundary, and cancellation is recorded but cannot produce `CancelledNoEffect` or interrupt recovery-critical verification and rollback

#### Scenario: a cancelled operation is submitted again

- **WHEN** the caller repeats an operation in `CancelledNoEffect` with the same repository ID, operation ID, and canonical request hash while its outcome is retained
- **THEN** the existing cancellation result and cleanup status are returned without restarting execution
- **AND THEN** after epoch compaction any replay returns `OperationEpochRetired`; retrying the work requires a current-epoch operation ID, while reusing a retained cancelled ID for a different payload returns `OperationConflict`

#### Scenario: stale reviewed plan is applied

- **WHEN** a file, repository revision, path type, symlink target, or other declared precondition changes after review and the change is detected before the first repository effect
- **THEN** apply returns `RejectedStale` before writing any path

#### Scenario: an apply owner expires before its first effect and preflight is stale

- **WHEN** an `Applying` owner lease expires before `FirstEffectStarted` and a reviewed dependency drifts before startup or status reconciliation
- **THEN** the reconciler acquires the fenced repository reservation and proves from the intact ledger/journal that no first-effect permit or repository effect was possible
- **AND THEN** it reruns final preflight, records the observed drift, and transitions `Reconciling` to `RejectedStale`
- **AND THEN** it performs only idempotent outside-repository control-artifact cleanup, does not roll back the external change, and records no repository effect
- **AND THEN** if no-effect proof is incomplete or contradictory, it records `RecoveryRequired` instead of `RejectedStale`

#### Scenario: a reviewed precondition drifts after an effect

- **WHEN** revision, dependency, path, identity, hash, permission, link, or another declared precondition changes after at least one effect has been journalled
- **THEN** the operation records the observed drift and performs no remaining planned effect
- **AND THEN** it rolls back from the durable journal and terminates as `RolledBack` only after complete verification, otherwise as `RecoveryRequired`
- **AND THEN** it is not reported as `RejectedStale` or `Applied`

#### Scenario: write fails mid-apply and rollback succeeds

- **WHEN** a staged multi-path apply encounters a write, replace, delete, move, verification, or cancellation failure after side effects begin
- **AND WHEN** rollback restores and verifies every precondition
- **THEN** the operation returns `RolledBack`
- **AND THEN** it is not reported as applied

#### Scenario: rollback is interrupted or incomplete

- **WHEN** rollback cannot restore and verify every affected path
- **THEN** the operation enters durable intervention state `RecoveryRequired`
- **AND THEN** its journal, backups, observed hashes, affected paths, and deterministic recovery guidance are retained
- **AND THEN** the operation is not reported as rolled back or merely failed

#### Scenario: manual recovery succeeds

- **WHEN** an authenticated `RecoveryOperator` or `RepositoryLifecycleAdministrator` resumes or rolls back an operation in `RecoveryRequired`
- **THEN** recovery acquires the same exclusive repository reservation and complete external-write/conditional-effect protection, advances the fencing epoch, and appends `Recovering` before any repository effect
- **AND THEN** successful verified resume terminates as `RecoveredApplied` and successful verified rollback terminates as `RecoveredRolledBack`
- **AND THEN** the original `RecoveryRequired` event and terminal recovery evidence remain in logical append-only history, with only eligible older attempt detail subject to authenticated bounded compaction
- **AND THEN** the plan and recovery bundle are released only after verified cleanup

#### Scenario: manual recovery remains incomplete

- **WHEN** a `Recovering` attempt cannot prove the requested applied or rolled-back state
- **THEN** it commits a bounded immutable attempt summary, atomically replaces `CurrentRecoveryCheckpoint` with the exact latest actionable evidence, and returns to `RecoveryRequired`
- **AND THEN** the plan, journal, backups, affected-path state, and recovery guidance remain pinned

#### Scenario: repeated incomplete recovery attempts exceed the hot-history window

- **WHEN** an authenticated recovery-authorized caller requests another recovery after enough fully quiesced inconclusive attempts to fill the per-operation hot count/byte window
- **THEN** before any new effect the authority folds eligible old attempt digests into `RecoveryAttemptAccumulator`, advances `CompactedThroughRecoveryAttempt`, and reuses only the pre-reserved attempt slot
- **AND THEN** it retains `CurrentRecoveryCheckpoint`, the bounded active recovery-artifact set, the original plan/base journal/backups, and every unresolved intent or unquiesced attempt needed for subsequent recovery
- **AND THEN** failure to prove compaction or checkpoint continuity returns `RecoveryAttemptUnavailable` with the recovery barrier intact and no repository effect
- **AND THEN** repeated attempts cannot increase coordination-store use beyond the declared per-operation budget or prevent a later admitted attempt from recording its truthful result

#### Scenario: unresolved recovery is explicitly acknowledged

- **WHEN** an authenticated `RecoveryAcknowledger` or `RepositoryLifecycleAdministrator` deliberately acknowledges an operation in `RecoveryRequired` without verified recovery and supplies a reason
- **THEN** the operation appends terminal state `RecoveryAcknowledged` without performing a repository effect
- **AND THEN** it records actor, time, reason, affected paths, last observed hashes, unresolved warnings, and that repository cleanliness was not verified
- **AND THEN** bulky recovery artifacts follow the caller's explicit retention choice while an immutable audit summary remains for terminal-history retention

#### Scenario: another plan targets a repository awaiting recovery

- **WHEN** a repository has an operation in `RecoveryRequired` and a different apply request from the same or another OS principal reaches final validation
- **THEN** the cross-principal registry and coordination domain expose the same barrier and reject the new apply without a repository effect until verified recovery or explicit acknowledgement lifts it

#### Scenario: cancellation arrives after side effects begin

- **WHEN** an authenticated caller authorized for `Cancel` requests cancellation after apply has begun modifying repository state
- **THEN** the request records cancellation but continues recovery-critical verification or rollback
- **AND THEN** the terminal state truthfully reports the observed repository condition

#### Scenario: the apply process exits after a side effect may have started

- **WHEN** an operation record remains `Applying` after its owner lease expires and `FirstEffectStarted` exists or durable evidence cannot exclude an effect
- **THEN** process startup or `GetOperation` first closes and drains the effect fence or proves worker termination and write-capability closure
- **AND THEN** proven quiescence permits one reconciler to advance the epoch and transition the record to `Reconciling`, while unproven quiescence records `OwnerQuiescencePending` and admits no reconciliation effect
- **AND THEN** after proven quiescence the reconciler verifies the persisted plan, journal, backups, staged files, affected-path identities, and observed hashes
- **AND THEN** that reconciler completes a provably finished apply, resumes or rolls back only from a declared journal boundary, or enters `RecoveryRequired`
- **AND THEN** the operation is never left or reported as a healthy live `Applying` owner after expiry; an unquiesced holder remains explicitly intervention-blocked

#### Scenario: a manual recovery owner exits after a side effect

- **WHEN** an operation record remains `Recovering` after its owner lease expires
- **THEN** a reconciler advances the fencing epoch only after the old effect fence is closed and drained or worker termination/write-capability closure is verified; otherwise it records `OwnerQuiescencePending`
- **AND THEN** after quiescence it acquires the same repository reservation and complete external-write/conditional-effect protection and preserves the requested recovery action
- **AND THEN** it reaches `RecoveredApplied`, `RecoveredRolledBack`, or `RecoveryRequired` from the durable journal rather than replaying an unproven effect

#### Scenario: an expired apply owner resumes after reconciliation starts

- **WHEN** a reconciler acquires the exclusive repository transaction reservation and advances the fencing epoch for an expired owner
- **AND WHEN** it first proves the old worker terminated with all write capabilities closed or closes and drains the non-bypassable effect fence
- **AND WHEN** it reacquires the persisted plan's complete external-write/conditional-effect protection before any repository effect
- **AND WHEN** the original owner later resumes with its stale epoch
- **THEN** the original owner cannot reacquire the reservation, pass effect admission, or append a journalled effect
- **AND THEN** it performs no replace, move, delete, backup, restore, or other repository write
- **AND THEN** takeover tests suspend and resume owners before and after every reservation, epoch, and effect boundary to prove stale writers are excluded

#### Scenario: an expired owner does not release the transaction reservation

- **WHEN** bounded shutdown cannot prove the mutation worker exited and the authority cannot prove an OS-revocable effect fence is closed and drained
- **THEN** the authority does not revoke or advance the owner's epoch and records nonterminal `OwnerQuiescencePending` with the possibly in-flight journal boundary
- **AND THEN** the existing reservation and an authority-wide intervention barrier block reconciliation effects, recovery acknowledgement, transfer, retirement, and queued plans while operator-visible termination or fence reconciliation proceeds
- **AND THEN** only verified worker termination with all write capabilities closed, or a closed and drained non-bypassable effect broker, permits epoch advancement and transition to `Reconciling`
- **AND THEN** the operation is not reported as a healthy live owner or as `RecoveryRequired` while the old holder may still execute

### Requirement: indeterminate mutation dispatch is never replayed automatically

Mutation transport SHALL classify caller knowledge as `NotDispatched`, `Completed`, or `Indeterminate`. An `Indeterminate` mutation SHALL NOT be retried or run in-process automatically. Cancellation before any request bytes are accepted MAY return `NotDispatched`. Once any request bytes are accepted, cancellation or timeout of the caller's wait SHALL return `Indeterminate`, never an ordinary cancelled operation result, even if `Received` is not yet durable. The receiver SHALL NOT use transport cancellation as permission to abandon the intake critical section without durable resolution.

After pre-receipt cancellation, the caller SHALL retain the same operation ID, send no
new apply, and query status. Receipt creation and authoritative `NotRecorded`
resolution SHALL contend under the same intake fence. If `Received` wins, status SHALL
expose that canonical operation and explicit idempotent cancellation SHALL target that
identity; if `NotRecorded` wins, a delayed receiver SHALL be rejected. While a receiver
lease remains in flight, status SHALL remain indeterminate rather than infer absence.
An intake receiver SHALL register a bounded renewable `IntakeAttempt` lease under the
fence; status SHALL create `NotRecorded` only after the lease is absent or expires,
and a later receiver SHALL require the one-use replay epoch rather than revive the
original attempt.
An explicit same-operation resubmission SHALL be permitted only after status lookup
durably returns authoritative `NotRecorded` with a replay epoch and only before the
operation epoch seals. A sealed operation epoch SHALL return `OperationEpochSealed`
for an otherwise unknown ID; a retired operation epoch SHALL return
`OperationEpochRetired`. Neither SHALL authorize replay.

#### Scenario: cancellation races durable intake

- **WHEN** a caller cancels after mutation request bytes are accepted but before it has received proof of durable `Received`
- **THEN** transport returns `Indeterminate` with the caller-known repository/operation identity and does not report ordinary operation cancellation
- **AND THEN** the caller submits no new apply and resolves that identity through status under the intake fence
- **AND THEN** either receipt creation wins and explicit cancellation targets the resulting canonical operation, or authoritative `NotRecorded` wins and fences the delayed receiver
- **AND THEN** an in-flight receiver's bounded `IntakeAttempt` lease keeps status indeterminate until the attempt resolves or expires

#### Scenario: cancellation occurs before dispatch

- **WHEN** cancellation wins before any mutation request bytes are accepted
- **THEN** transport may return `NotDispatched` and no canonical operation result is manufactured
- **AND THEN** operation policy may permit later dispatch of the same canonical request and caller-known identity, but no accepted operation result is inferred

#### Scenario: daemon response is lost after dispatch

- **WHEN** a mutation request reaches or may have reached the daemon but its response is unavailable
- **THEN** the client returns an indeterminate transport result with the caller-known repository/operation identity and status lookup guidance
- **AND THEN** it sends no second apply request and uses no in-process fallback while status remains indeterminate
- **AND THEN** it may explicitly resubmit only after authoritative `NotRecorded`, using the same operation ID, request hash, and returned replay epoch

#### Scenario: daemon cannot connect before dispatch

- **WHEN** the transport proves that no request bytes were accepted
- **THEN** the host may execute the same canonical operation in-process according to declared policy
- **AND THEN** it preserves the operation ID and operation contract

#### Scenario: apply-capable command uses the legacy daemon bridge

- **WHEN** a command can modify files but has no typed operation and safe transport contract
- **THEN** it is excluded from daemon routing and the stable MCP surface

### Requirement: workspace completeness governs confidence and advice

Before acquisition, an operation SHALL declare a semantic-input manifest containing every requested project and transitive project reference; the selected target framework, configuration, and compilation context for each project, or every applicable context when the request does not narrow them; source, additional, analyzer-config, and generated documents; project, metadata, framework, SDK, package, and imported build inputs; and every evaluator, source-generator, or analyzer stage whose output the operation requires. Workspace acquisition SHALL return `Complete`, `Partial`, or `Failed`, the per-input loaded/resolved/failed/policy-blocked/excluded state, requested/loaded/failed project identities, all diagnostics and their completeness impact, source identity, revision, SDK/compiler configuration, and independent trust-state/evaluator-mode provenance. `Complete` SHALL require every required manifest entry at the declared revision and successful completion of every required evaluation/generator stage. An excluded input SHALL count as complete only when the operation contract declares and validates that it is irrelevant. `Partial` SHALL mean at least one usable semantic scope exists but a required input is unavailable, unresolved, failed, or blocked. `Failed` SHALL mean no usable semantic model remains or the missing input prevents the operation's minimum declared scope. Partial workspaces SHALL not produce proof-strength conclusions, deletion advice, or automatic mutation suggestions.

#### Scenario: project objects load but semantic input is missing

- **WHEN** all requested project objects are created but a required target framework, configuration, metadata/project/framework reference, imported build input, generated document, or required generator/analyzer stage is unavailable or fails
- **THEN** the workspace is `Partial` when a usable declared scope remains, otherwise `Failed`
- **AND THEN** it is not labelled `Complete` merely because every project object exists

#### Scenario: a request intentionally narrows compilation scope

- **WHEN** a request explicitly selects one target framework or compilation context and the operation contract permits that scope
- **THEN** completeness is evaluated against that declared scope and records omitted contexts in provenance
- **AND THEN** an omitted input is treated as irrelevant only when the operation validates that it cannot affect the requested conclusion

#### Scenario: project load fails during unused analysis

- **WHEN** one or more requested projects fail to load
- **THEN** `find-unused` returns partial status and affected findings are downgraded
- **AND THEN** no safe-delete suggestion is emitted

#### Scenario: workspace warnings are hidden from the text view

- **WHEN** human-facing output suppresses non-fatal workspace diagnostics
- **THEN** structured provenance still contains the complete diagnostic set, per-input manifest state, diagnostic completeness impact, and completeness decision

#### Scenario: workspace contains no usable projects

- **WHEN** no requested project is loaded into a usable semantic model
- **THEN** workspace acquisition returns `Failed`
- **AND THEN** the operation does not emit an empty successful analysis

### Requirement: proof-strength unused findings require a declared closed world

An unused finding SHALL include its evidence, confidence basis, and limitations. A publicly or externally visible symbol SHALL NOT receive proof-strength unused status unless the operation declares and verifies the relevant closed-world boundary, including interface/override dispatch and supported framework, reflection, generated-code, markup, and external-consumer assumptions.

#### Scenario: public symbol has no in-solution references

- **WHEN** a public symbol has zero references in the loaded solution but external consumers are possible
- **THEN** the result is not classified as proven unused
- **AND THEN** no automatic safe-delete advice is emitted

#### Scenario: unsupported dynamic usage may exist

- **WHEN** a finding can be used by an unsupported reflective, generated, framework, or markup mechanism
- **THEN** the finding reports that limitation and is downgraded or suppressed according to policy

### Requirement: agent-facing contracts are executable

Stable command metadata, schemas, defaults, aliases, primer statements, safety, examples, and follow-on actions SHALL be generated or verified from the same capability catalogue. Invalid input SHALL be an explicit structured error.

#### Scenario: an invalid enum option is supplied

- **WHEN** a user supplies an unsupported scope or mode
- **THEN** the operation returns `InvalidInput`
- **AND THEN** it does not return an empty successful analysis result

#### Scenario: a published example drifts

- **WHEN** a documented command example no longer binds or produces the declared result shape against its fixture
- **THEN** CI fails

### Requirement: MCP behaviour conforms to every advertised protocol version

The MCP host SHALL either use the official C# SDK or pass spawned-process conformance tests for each advertised protocol version. It SHALL obey request and notification semantics, version negotiation/lifecycle requirements, cancellation, schema, and structured-result rules for that version.

#### Scenario: a notification is received

- **WHEN** the client sends a valid MCP notification without an ID
- **THEN** the server performs the notification behaviour or ignores it according to the negotiated protocol
- **AND THEN** it sends no JSON-RPC response

#### Scenario: malformed JSON is received

- **WHEN** input is not valid JSON
- **THEN** the server returns the protocol's parse-error response where a response is permitted
- **AND THEN** the server remains available for subsequent messages

#### Scenario: client requests an unsupported protocol version

- **WHEN** the client and server have no mutually supported protocol version
- **THEN** initialisation fails with a structured protocol error
- **AND THEN** tools are not exposed under a falsely claimed version

#### Scenario: a structured tool succeeds

- **WHEN** a tool with an output schema returns data
- **THEN** its `structuredContent` validates against that schema
- **AND THEN** an optional text content view describes the same result without being the machine contract

#### Scenario: a request is cancelled

- **WHEN** the authenticated client cancels an in-flight request it owns or is currently authorized to cancel
- **THEN** cancellation reaches the canonical operation
- **AND THEN** the server remains able to receive and process protocol messages while that operation unwinds
- **AND THEN** the same request ID on another connection or an unauthorized principal cannot cancel or learn the operation

### Requirement: resource consumption is bounded and observable

Stable operations SHALL declare and enforce applicable limits for request size, output size, item count, execution time, concurrency, paging, and memory-sensitive work. Cancellation tokens SHALL be threaded through workspace, Roslyn, filesystem, and host operations where cancellation is safe. Every resumable continuation token SHALL be opaque and integrity protected and SHALL bind the schema/operation identity, canonical request hash with the token omitted, canonical repository/workspace identity, source revision plus semantic-input/evaluator fingerprint, projection, filters, total ordering and tie-break rules, page-size policy, cursor, and bounded expiry. A continuation request SHALL be authorized again; the token SHALL NOT grant access or elevate trust.

The operation SHALL either serve every page from the exact retained immutable snapshot
under bounded lifetime/byte/count quotas or reject a stale, expired, tampered,
unauthorized, or otherwise mismatched token with a typed error. It SHALL NOT silently
reevaluate a later workspace revision beneath an earlier cursor. If safe bounded
snapshot continuation is unavailable, the operation SHALL return deterministic
truncation with an explicit limitation marker and no resumable token.

#### Scenario: a result exceeds its output budget

- **WHEN** an analysis result exceeds the declared item or byte limit
- **THEN** the response is deterministically paged or truncated with an explicit continuation/limitation marker
- **AND THEN** a continuation token binds the canonical request, exact source/workspace revision and evaluator inputs, projection, total ordering, cursor, and expiry, while a truncated result without a retained snapshot is explicitly non-resumable
- **AND THEN** the host does not silently allocate or emit an unbounded payload

#### Scenario: workspace inputs change between pages

- **WHEN** a client presents a valid continuation after the checkout, semantic inputs, evaluator fingerprint, projection, filter, or ordering differs from the token binding
- **THEN** the operation serves the page only from the exact retained snapshot while current authorization and trust still permit access, or returns a typed stale continuation error
- **AND THEN** it never combines findings from the changed workspace with the earlier result set

#### Scenario: continuation authorization or trust changes

- **WHEN** the current principal, authorization policy, or authoritative repository trust decision no longer permits the bound continuation
- **THEN** the operation returns a typed unauthorized continuation error even if the exact snapshot is still retained
- **AND THEN** the token grants no access and cannot restore the earlier trust state

#### Scenario: a long operation is cancelled safely

- **WHEN** cancellation is requested during a cancellable non-mutating analysis, or after a mutation has a durable `Received` receipt but before its first repository effect
- **THEN** the non-mutating analysis stops promptly, while the accepted mutation uses the canonical cancellation arbitration and returns a structured cancellation outcome only after that outcome is durable
- **AND THEN** if mutation request bytes were accepted but durable `Received` is not yet proven, transport instead returns `Indeterminate` and the caller resolves the same repository/operation identity through status or authoritative `NotRecorded` without dispatching new work

### Requirement: the daemon is transparent and locally isolated in this phase

The daemon SHALL be an optional typed transport adapter only. It SHALL NOT cache workspaces, own mutation/idempotency semantics, route apply-capable legacy CLI commands, mutate process-global request state, log raw request payloads, retry after indeterminate mutation dispatch, change the execution operation, or alter typed observable results. Its endpoint SHALL be per-user and collision-resistant, and every connection SHALL authenticate both the client principal and daemon instance before request bytes or source-bearing results are exchanged. Authorization SHALL use OS-enforced peer identity plus endpoint/container access control, or cryptographically equivalent mutually authenticated local transport with user-private OS/service-protected discovery and credentials outside repository control. If endpoint creation, discovery, credential protection, peer authentication, or authorization cannot be proven, daemon startup SHALL fail and no endpoint SHALL be advertised; a host MAY select canonical in-process execution only before dispatch.

#### Scenario: two users run the daemon on one machine

- **WHEN** separate users start the daemon
- **THEN** their endpoints and state cannot be confused or accessed through one global well-known name

#### Scenario: authenticated endpoint protection is unavailable

- **WHEN** the platform cannot prove endpoint access control or mutually authenticated local transport, including protected discovery and credential storage
- **THEN** daemon startup fails closed and advertises no endpoint
- **AND THEN** clients do not send request bytes, may select canonical in-process execution before dispatch, and never use endpoint-name secrecy as authorization

#### Scenario: another principal or a rogue daemon attempts a connection

- **WHEN** a client or server cannot prove the expected authenticated principal and daemon-instance identity
- **THEN** the connection is rejected before request content, repository existence, operation status, or source-bearing results are disclosed

#### Scenario: request contains source or secret-bearing input

- **WHEN** a request is logged for diagnostics
- **THEN** logs contain bounded structured metadata and redacted diagnostics
- **AND THEN** raw command payload, source text, piped input, and arbitrary environment values are not persisted

### Requirement: normal builds are hermetic

A normal restore/build/test SHALL NOT modify tracked source or documentation, format files, deploy skills or binaries into the user profile, start a daemon, or require previously deployed global state. Generation, formatting, deployment, dogfooding, and mutation testing SHALL be explicit opt-in commands or targets.

#### Scenario: a clean checkout is built in Debug

- **WHEN** `dotnet build` runs on a clean checkout
- **THEN** the Git working tree remains unchanged
- **AND THEN** no file outside declared build-output/temp directories is created or modified

### Requirement: release identity and installation claims are verifiable

Assembly, NuGet, MCP server manifest, archive, and release versions SHALL derive from one release source. Documentation SHALL advertise only installation paths that the release pipeline actually publishes. Install, upgrade, rollback, and uninstall SHALL preserve unowned files and recover from interrupted replacement.

#### Scenario: repository has no stable release

- **WHEN** no stable tag and verified artifacts exist
- **THEN** documentation and manifests do not advertise an installable stable `1.0.0` release

#### Scenario: supported RID artifact is released

- **WHEN** an artifact is attached to a release
- **THEN** native-OS CI has installed it, launched CLI and MCP, exercised a fixture operation, verified checksum/provenance, and tested uninstall

#### Scenario: documented NuGet installation is unavailable

- **WHEN** the release pipeline does not publish the documented .NET tool package
- **THEN** the documentation claim is removed or the package publication becomes a release gate
