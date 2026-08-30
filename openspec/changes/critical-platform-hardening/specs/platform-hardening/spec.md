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

A workspace request SHALL declare repository trust state and evaluator mode as independent values. Trust state SHALL be `Trusted` or `Untrusted`; evaluator mode SHALL be `NoEvaluation`, `InProcess`, or `IsolatedWorker`. Absence or revocation of an explicit repository-scoped trust decision SHALL produce `Untrusted`. An untrusted request SHALL NOT select `InProcess`, while a trusted request MAY still select `IsolatedWorker`. The long-lived CLI, MCP, and daemon hosts SHALL NOT evaluate repository-controlled MSBuild logic, analyzers, generators, or repository executables in-process for an untrusted repository. `IsolatedWorker` SHALL use an OS-enforced allow-listed filesystem namespace that exposes only declared read-only inputs and disposable writable outputs; it SHALL NOT expose ambient host paths or credentials.

Every `IsolatedWorker` phase capable of executing untrusted repository-controlled code
SHALL run in an OS-enforced networkless namespace with no DNS, socket, loopback, proxy,
or external egress; policy SHALL NOT enable networking inside that phase. Required
SDK/package/tool acquisition SHALL occur beforehand through a separate trusted broker
that executes no repository code, accepts only policy-allow-listed registries and
user-reviewed lockfile-pinned coordinates, rejects repository-generated URLs, proxy
settings, restore hooks, and dynamic evaluator output, verifies content hashes and
signatures, and exposes results as declared read-only worker inputs. Missing inputs
SHALL produce a typed diagnostic rather than trigger restore inside the worker. A
platform that cannot prove both filesystem containment and egress denial SHALL refuse
untrusted evaluation.

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

#### Scenario: isolated repository code attempts network egress

- **WHEN** repository-controlled MSBuild logic, an analyzer, a generator, or a child process attempts DNS, socket, loopback, proxy, or external network access during untrusted evaluation
- **THEN** the OS-enforced networkless namespace denies the attempt and no checkout or declared input content leaves the worker
- **AND THEN** network access cannot be enabled for any phase in which untrusted repository code executes
- **AND THEN** missing SDK/package/tool inputs produce a typed diagnostic rather than an in-worker restore

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

### Requirement: request context is explicit and host-neutral

Repository root, solution path, configuration, trust state, evaluator mode, resource budget, request ID, and operation ID SHALL travel as explicit request context. An operation SHALL NOT depend on or mutate process current directory, arbitrary process environment, global console state, or `Environment.ExitCode` as request context.

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
terminal cleanup or explicit recovery acknowledgement.

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

- **WHEN** normal plan-retention time elapses while an associated operation is `Applying`, `Reconciling`, `Recovering`, or `RecoveryRequired`
- **THEN** garbage collection retains the exact plan bytes and their journal, backup, and recovery references as one complete bundle
- **AND THEN** it releases them only after verified terminal cleanup or explicit recovery acknowledgement

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
operation epochs, apply-capable preparation, intake, replay promotion, and concurrent
ownership changes, and seal open operation epochs while leaving status, cancellation,
cleanup, and recovery available.

Before transfer commits, the authority SHALL use the existing repository reservation
and coordination generation to wait for every live owner or reconcile it after lease
expiry and proven reservation release. It SHALL prove no intake attempt/receipt
promotion is in flight, no transaction reservation or live owner remains, every
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
- **AND THEN** it proves no intake lease, receipt promotion, reservation, live owner, nonterminal operation, or replay authorization remains before changing ownership
- **AND THEN** it carries forward retained/pinned records and epoch state, increments `CoordinationGeneration`, rotates credentials, and only then releases the transfer fence
- **AND THEN** an old owner cannot mutate with its prior token, plan proof, lease, or fencing generation

#### Scenario: ownership transfer races an active apply

- **WHEN** transfer begins while an operation is `Applying`, `Recovering`, `Reconciling`, or otherwise nonterminal
- **THEN** no new mutation intake is admitted and transfer does not change the ownership domain or repository identity yet
- **AND THEN** the active owner completes under the old reservation, or an expired owner is fenced and reconciled under that same reservation
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
Canonical repository ID plus operation ID SHALL be the composite durable identity
for intake, idempotency, status, recovery, acknowledgement, and retention; operation
IDs may repeat across repositories. Every operation lookup or mutation SHALL carry
both values. Before accepted work can enter an execution queue, the receiver SHALL
acquire a cross-process, store-enforced intake fence keyed by repository and operation
ID; validate the token, claims hash, epoch/ID match, and authenticated-principal
binding; reserve bounded worst-case receipt/ledger/outcome/recovery-metadata capacity;
and durably record a `Received` receipt containing the repository ID, operation ID,
request hash, and canonical request. The operation ledger SHALL then be recorded
before repository side effects begin. A `Received` receipt without an operation
record SHALL be reconciled on startup and status lookup. Token validation, the
epoch-open/quota check, capacity reservation, and receipt creation SHALL be atomic
with respect to epoch sealing and concurrent admission.

Every terminal transition, including durable expiry, SHALL atomically retain a
compact outcome record keyed by canonical repository ID and operation ID for a
declared minimum replay window. That record SHALL preserve request and plan hashes,
terminal state, canonical outcome, completion time, final evidence digest, and
required cleanup/recovery summary. The intake fence SHALL consult active and retained
records before creating `Received` or `NotRecorded`.

The authority SHALL bound per-repository and per-principal open operations,
simultaneously open/unretired epochs, accepted operation count/bytes per epoch,
epoch issuance rate, replay-window duration, terminal-record bytes, and pinned
recovery/cleanup exceptions. It SHALL seal an epoch at its token's `NotAfter` or
earlier quota, accept no unknown operation ID or replay authorization in a sealed
epoch, and return typed `CoordinationCapacityExceeded` before intake rather than mint
unlimited epochs when admission limits are exhausted. Existing live and retained
records SHALL remain queryable after sealing. Sealing SHALL atomically transition
every outstanding `NotRecorded` record to terminal `Expired` and invalidate its
replay authorization while already accepted operations continue. Status and recovery
SHALL remain available while mutation admission is blocked. Quotas SHALL NOT prevent an accepted
operation from recording any truthful transition, terminal outcome, cleanup state, or
recovery result within its declared resource budget.

After the replay window, the authority SHALL atomically fold eligible terminal
digests into a cumulative `RetiredOperationAccumulator`, advance a monotonic
`RetiredThroughEpoch` watermark, and only then remove individual outcome records.
Nonterminal, `RecoveryRequired`, and cleanup-pending operations SHALL remain in a
bounded pinned-exception index; reaching its cap SHALL block new mutation admission.
Lookup SHALL resolve pinned/live records before the watermark. An otherwise unknown
operation at or below the watermark SHALL return `OperationEpochRetired` and SHALL
NOT create `NotRecorded`, authorize replay, or execute. The watermark and accumulator
SHALL never move backward. Repository/epoch identifiers SHALL never be reused, and
repository retirement SHALL preserve the final watermark/accumulator in one permanent
retired-repository marker.

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
- **THEN** the store first commits a permanent retired-repository marker containing the final operation-epoch watermark and accumulator
- **AND THEN** the retired repository ID is never reused and every later request carrying it is rejected before intake

#### Scenario: the first response is lost

- **WHEN** an apply request may have reached the execution host but its response is unavailable
- **THEN** the caller still knows the operation ID
- **AND THEN** it can query operation status without generating a second apply operation

#### Scenario: execution dies before operation-ledger creation

- **WHEN** dispatch is indeterminate, the operation epoch remains open, and status lookup finds neither a durable intake receipt nor an operation record
- **THEN** status lookup validates the original operation-epoch token and an `OperationRequestIdentity` containing request hash, plan ID/hash, expected revision, and operation-epoch claims hash
- **AND THEN** it proves the referenced plan is accessible, acquires the same operation-intake fence, and proves no receiver for that operation ID remains in flight
- **AND THEN** the epoch-open check and durable `NotRecorded` creation are atomic with sealing, and the record pins that plan and returns a replay epoch
- **AND THEN** a delayed original attempt without that epoch is rejected before execution
- **AND THEN** the caller may explicitly resubmit the exact request before the operation epoch seals, using the same operation ID, request hash, and replay epoch to transition `NotRecorded` to `Received` exactly once
- **AND THEN** this path performs no automatic retry and no in-process fallback

#### Scenario: an identical operation is repeated

- **WHEN** the same operation ID is submitted with the same canonical plan, request hash, and expected revision while its live or retained outcome record exists
- **THEN** the existing intake receipt, operation state, or terminal result is returned
- **AND THEN** repository side effects are not applied again

#### Scenario: an operation ID is reused for different work

- **WHEN** a live or retained operation ID is submitted with a different plan, payload, or expected revision
- **THEN** the request returns `OperationConflict`
- **AND THEN** no repository path is written

#### Scenario: an operation is repeated after its epoch is compacted

- **WHEN** a request carries an operation ID whose live/retained record is absent and whose epoch is at or below `RetiredThroughEpoch`
- **THEN** lookup returns `OperationEpochRetired`
- **AND THEN** it does not create `NotRecorded`, disclose a deleted outcome, distinguish payload hashes, authorize replay, or execute repository work

#### Scenario: two repositories use the same structured operation value

- **WHEN** two canonical repositories submit different work under the same epoch/nonce value authenticated by their respective repository-scoped tokens
- **THEN** their composite repository/operation identities address independent receipts, ledgers, status, recovery, and retention
- **AND THEN** status, resume, rollback, and acknowledgement require the canonical repository ID and cannot observe or mutate the other repository's operation

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
and guard/version tokens and grant the fenced owner permission to issue exactly that
effect. After owner loss, the marker SHALL be treated as possibly effected until
reconciliation proves its observed outcome from durable evidence.
Every side effect and every expired-owner takeover SHALL validate the reservation,
external-write guards or conditional-effect tokens, current ledger state, epoch,
lease, expected intermediate repository state, prior effects, remaining plan
dependencies, platform file identity, and link count. An expired owner SHALL be
reconciled from durable evidence. The ledger SHALL retain an append-only transition
history. Direct terminal states SHALL distinguish `Applied` and `RolledBack`;
verified post-recovery terminal states SHALL distinguish `RecoveredApplied` and
`RecoveredRolledBack`; `RecoveryAcknowledged` SHALL be an administrative terminal
state that makes no repository-cleanliness claim. `CancelledNoEffect`,
`RejectedStale`, `RejectedConflict`, `RejectedUnsafeCoordination`, and `Expired` SHALL
remain no-effect terminal outcomes. `RecoveryRequired` SHALL be a durable intervention state, while `Received`,
`NotRecorded`, `Prepared`, `Applying`, `Recovering`, and `Reconciling` are nonterminal
or dormant resolution states.

An apply reconciler MAY transition from `Reconciling` to `CancelledNoEffect`,
`RejectedStale`, `RejectedUnsafeCoordination`, or `Expired` only while holding the
fenced repository reservation and after verifying an intact ledger/journal containing
no `FirstEffectStarted` permit and proving no repository effect was possible. It SHALL
rerun final preflight before selecting the applicable no-effect outcome and SHALL NOT
roll back unrelated external drift. If no-effect proof is absent or contradictory, it
SHALL resolve the possibly effected journal to `Applied` or `RolledBack`, or retain
evidence in `RecoveryRequired`.

#### Scenario: two apply plans target the same repository

- **WHEN** two apply or recovery requests for the same canonical repository overlap
- **THEN** only one acquires the exclusive transaction reservation and enters final whole-plan preflight
- **AND THEN** the other waits, cancels, or rejects before final validation and performs no repository side effect
- **AND THEN** the owner holds the reservation through every journalled effect and durable terminal result or explicit recovery handoff
- **AND THEN** each effect revalidates the expected intermediate state and every still-relevant reviewed dependency, so neither a queued plan nor external drift can be silently accepted

#### Scenario: an accepted apply is cancelled before its first repository effect

- **WHEN** an operation with a durable `Received` receipt is cancelled before `FirstEffectStarted`, including while awaiting the repository reservation, staging, acquiring external-write protection, or running final preflight
- **THEN** cancellation uses the same cross-process intake fence when receipt-to-ledger promotion is still pending, and one store-enforced compare-and-swap records `CancelledNoEffect` under the same repository ID, operation ID, and request hash
- **AND THEN** neither receipt promotion nor the current owner can enqueue or start an effect after cancellation wins
- **AND THEN** the terminal record contains cancellation timing, journal sequence, proof that no first-effect permit exists, staged-artifact inventory, and cleanup status
- **AND THEN** startup and status lookup clean the outside-repository control artifacts idempotently without writing the repository, and the exact plan remains pinned until cleanup is verified

#### Scenario: cancellation races the first repository effect

- **WHEN** cancellation and the first guarded repository effect become eligible concurrently
- **THEN** one durable compare-and-swap either commits `CancelledNoEffect` or appends `FirstEffectStarted` with the complete first-effect intent and guard/version evidence and grants the current fenced owner permission to issue exactly that effect
- **AND THEN** if cancellation wins, no repository effect can start
- **AND THEN** if the effect permit wins, cancellation is recorded but cannot produce `CancelledNoEffect` or interrupt recovery-critical verification and rollback

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

- **WHEN** an authorized caller resumes or rolls back an operation in `RecoveryRequired`
- **THEN** recovery acquires the same exclusive repository reservation and complete external-write/conditional-effect protection, advances the fencing epoch, and appends `Recovering` before any repository effect
- **AND THEN** successful verified resume terminates as `RecoveredApplied` and successful verified rollback terminates as `RecoveredRolledBack`
- **AND THEN** the original `RecoveryRequired` event and the recovery attempt remain in append-only history
- **AND THEN** the plan and recovery bundle are released only after verified cleanup

#### Scenario: manual recovery remains incomplete

- **WHEN** a `Recovering` attempt cannot prove the requested applied or rolled-back state
- **THEN** it appends the attempt evidence and returns to `RecoveryRequired`
- **AND THEN** the plan, journal, backups, affected-path state, and recovery guidance remain pinned

#### Scenario: unresolved recovery is explicitly acknowledged

- **WHEN** an authorized caller acknowledges an operation in `RecoveryRequired` without verified recovery
- **THEN** the operation appends terminal state `RecoveryAcknowledged` without performing a repository effect
- **AND THEN** it records actor, time, reason, affected paths, last observed hashes, unresolved warnings, and that repository cleanliness was not verified
- **AND THEN** bulky recovery artifacts follow the caller's explicit retention choice while an immutable audit summary remains for terminal-history retention

#### Scenario: another plan targets a repository awaiting recovery

- **WHEN** a repository has an operation in `RecoveryRequired` and a different apply request from the same or another OS principal reaches final validation
- **THEN** the cross-principal registry and coordination domain expose the same barrier and reject the new apply without a repository effect until verified recovery or explicit acknowledgement lifts it

#### Scenario: cancellation arrives after side effects begin

- **WHEN** cancellation is requested after apply has begun modifying repository state
- **THEN** the request records cancellation but continues recovery-critical verification or rollback
- **AND THEN** the terminal state truthfully reports the observed repository condition

#### Scenario: the apply process exits after a side effect may have started

- **WHEN** an operation record remains `Applying` after its owner lease expires and `FirstEffectStarted` exists or durable evidence cannot exclude an effect
- **THEN** process startup or `GetOperation` atomically claims one reconciliation lease and transitions the record to `Reconciling`
- **AND THEN** the reconciler verifies the persisted plan, journal, backups, staged files, affected-path identities, and observed hashes
- **AND THEN** it completes a provably finished apply, resumes or rolls back only from a declared journal boundary, or enters `RecoveryRequired`
- **AND THEN** the operation cannot remain `Applying` indefinitely or be mistaken for a live owner

#### Scenario: a manual recovery owner exits after a side effect

- **WHEN** an operation record remains `Recovering` after its owner lease expires
- **THEN** the reconciler acquires the same repository reservation and complete external-write/conditional-effect protection, advances the fencing epoch, and preserves the requested recovery action
- **AND THEN** it reaches `RecoveredApplied`, `RecoveredRolledBack`, or `RecoveryRequired` from the durable journal rather than replaying an unproven effect

#### Scenario: an expired apply owner resumes after reconciliation starts

- **WHEN** a reconciler acquires the exclusive repository transaction reservation and advances the fencing epoch for an expired owner
- **AND WHEN** it reacquires the persisted plan's complete external-write/conditional-effect protection before any repository effect
- **AND WHEN** the original owner later resumes with its stale epoch
- **THEN** the original owner cannot reacquire the reservation or append a journalled effect
- **AND THEN** it performs no replace, move, delete, backup, restore, or other repository write
- **AND THEN** takeover tests suspend and resume owners before and after every reservation, epoch, and effect boundary to prove stale writers are excluded

#### Scenario: an expired owner does not release the transaction reservation

- **WHEN** bounded shutdown cannot prove the mutation worker exited and its OS reservation was released
- **THEN** the ledger epoch is revoked and the operation records `RecoveryRequired` with the possibly in-flight journal boundary
- **AND THEN** no reconciler or queued plan performs a repository effect until reservation release is proven
- **AND THEN** the operation does not remain indefinitely reported as a healthy live `Applying` owner

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

- **WHEN** the client cancels an in-flight cancellable operation
- **THEN** cancellation reaches the canonical operation
- **AND THEN** the server remains able to receive and process protocol messages while that operation unwinds

### Requirement: resource consumption is bounded and observable

Stable operations SHALL declare and enforce applicable limits for request size, output size, item count, execution time, concurrency, paging, and memory-sensitive work. Cancellation tokens SHALL be threaded through workspace, Roslyn, filesystem, and host operations where cancellation is safe.

#### Scenario: a result exceeds its output budget

- **WHEN** an analysis result exceeds the declared item or byte limit
- **THEN** the response is deterministically paged or truncated with an explicit continuation/limitation marker
- **AND THEN** the host does not silently allocate or emit an unbounded payload

#### Scenario: a long analysis is cancelled

- **WHEN** cancellation is requested before mutation side effects begin
- **THEN** the operation stops promptly and returns a structured cancellation outcome

### Requirement: the daemon is transparent and locally isolated in this phase

The daemon SHALL be an optional typed transport adapter only. It SHALL NOT cache workspaces, own mutation/idempotency semantics, route apply-capable legacy CLI commands, mutate process-global request state, log raw request payloads, retry after indeterminate mutation dispatch, change the execution operation, or alter typed observable results. Its endpoint SHALL be per-user, collision-resistant, and access controlled where the platform permits.

#### Scenario: two users run the daemon on one machine

- **WHEN** separate users start the daemon
- **THEN** their endpoints and state cannot be confused or accessed through one global well-known name

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
