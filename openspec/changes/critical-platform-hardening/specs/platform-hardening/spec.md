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

#### Scenario: file-only operation is safe without evaluation

- **WHEN** an untrusted repository invokes an operation whose contract requires no MSBuild, analyzer, generator, or repository executable
- **THEN** the operation may execute without elevating repository trust
- **AND THEN** provenance states that no repository-controlled evaluation was performed

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

A mutation SHALL prepare an immutable change set before apply. The plan SHALL describe every create, replacement, delete, move, project edit, Roslyn document, additional document, and analyzer-config document it can affect, including exact path preconditions, expected hashes/existence, platform file identity, link count, known hard-link aliases, encoding/newline/file-mode policy, symlink/hard-link policy, operation-scoped staging/backup roots outside the repository, OS/filesystem conditional-effect or external-write-guard requirements, diff, verification plan, expiry, and repository revision. Before prepare succeeds, the exact canonical plan bytes SHALL be atomically persisted in a user-private durable store scoped by canonical repository identity. Apply SHALL identify those bytes by repository ID, plan ID, and plan hash and SHALL NOT recompute them from command arguments or a newer workspace snapshot. Once an operation record references a plan, the store SHALL pin its exact bytes through all nonterminal states and through `RecoveryRequired` until verified terminal cleanup or explicit recovery acknowledgement.

#### Scenario: preview and apply use the same plan

- **WHEN** a prepared mutation is reviewed and then applied without drift
- **THEN** apply consumes the reviewed immutable plan
- **AND THEN** it does not recompute a semantically different mutation from the original command arguments

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

- **WHEN** a source/destination falls outside the canonical repository root, or a staging/backup/control path falls outside its user-private control root or overlaps the repository
- **THEN** preparation or preflight rejects the plan before any repository write

#### Scenario: staging is interrupted before repository effects

- **WHEN** apply is cancelled, crashes, or finds stale input while creating replacement content
- **THEN** a durable journal already identifies every staging intent and completed artifact
- **AND THEN** all staging and backup paths are under an operation-scoped user-private root outside the repository
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

### Requirement: apply operations have caller-known durable identity

Every apply request SHALL include a caller-generated operation ID and canonical
request hash before dispatch. The request hash SHALL be the SHA-256 digest of a
versioned UTF-8 canonical serialization containing every semantic work-defining
field, including repository ID, plan ID/hash, operation ID, and expected revision,
while omitting `RequestHash` itself, replay epoch, transport attempt/correlation
metadata, cancellation state, and authentication material. All adapters SHALL use
the same canonicalizer and test vectors rather than hashing transport-specific JSON.
Canonical repository ID plus operation ID SHALL be the composite durable identity
for intake, idempotency, status, recovery, acknowledgement, and retention; operation
IDs may repeat across repositories. Every operation lookup or mutation SHALL carry
both values. Before accepted work can enter an execution queue, the receiver SHALL
acquire a cross-process, store-enforced intake fence keyed by repository and operation
ID and durably record a `Received` receipt containing the repository ID, operation
ID, request hash, and canonical request. The operation ledger SHALL then be recorded
before repository side effects begin. A `Received` receipt without an operation
record SHALL be reconciled on startup and status lookup.

#### Scenario: a caller constructs the canonical request hash

- **WHEN** a caller prepares an apply request for dispatch
- **THEN** it serializes the semantic request with the shared schema version, path/string normalization, property ordering, explicit-null, and default-value rules
- **AND THEN** `RequestHash`, replay epoch, transport metadata, cancellation state, and authentication material are absent from the hashed bytes
- **AND THEN** CLI, MCP, in-process, and daemon adapters produce the same SHA-256 digest for the same semantic request

#### Scenario: the first response is lost

- **WHEN** an apply request may have reached the execution host but its response is unavailable
- **THEN** the caller still knows the operation ID
- **AND THEN** it can query operation status without generating a second apply operation

#### Scenario: execution dies before operation-ledger creation

- **WHEN** dispatch is indeterminate and status lookup finds neither a durable intake receipt nor an operation record
- **THEN** status lookup acquires the same operation-intake fence and proves no receiver for that operation ID remains in flight
- **AND THEN** it durably records and returns `NotRecorded` with the canonical request hash and a replay epoch
- **AND THEN** a delayed original attempt without that epoch is rejected before execution
- **AND THEN** the caller may explicitly resubmit the exact request with the same operation ID, request hash, and replay epoch, transitioning `NotRecorded` to `Received` exactly once
- **AND THEN** this path performs no automatic retry and no in-process fallback

#### Scenario: an identical operation is repeated

- **WHEN** the same operation ID is submitted with the same canonical plan, request hash, and expected revision
- **THEN** the existing intake receipt, operation state, or terminal result is returned
- **AND THEN** repository side effects are not applied again

#### Scenario: an operation ID is reused for different work

- **WHEN** an existing operation ID is submitted with a different plan, payload, or expected revision
- **THEN** the request returns `OperationConflict`
- **AND THEN** no repository path is written

#### Scenario: two repositories use the same operation ID

- **WHEN** two canonical repositories submit different work under the same caller-generated operation ID
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
`RejectedStale`, `RejectedConflict`, and `Expired` SHALL remain no-effect terminal
outcomes. `RecoveryRequired` SHALL be a durable intervention state, while `Received`,
`NotRecorded`, `Prepared`, `Applying`, `Recovering`, and `Reconciling` are nonterminal
or dormant resolution states.

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

- **WHEN** the caller repeats an operation in `CancelledNoEffect` with the same repository ID, operation ID, and canonical request hash
- **THEN** the existing cancellation result and cleanup status are returned without restarting execution
- **AND THEN** retrying the work requires a new operation ID, while reusing the cancelled operation ID for a different payload returns `OperationConflict`

#### Scenario: stale reviewed plan is applied

- **WHEN** a file, repository revision, path type, symlink target, or other declared precondition changes after review and the change is detected before the first repository effect
- **THEN** apply returns a stale-plan rejection before writing any path

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

- **WHEN** a repository has an operation in `RecoveryRequired` and a different apply request reaches final validation
- **THEN** the new apply is rejected without a repository effect until verified recovery or explicit acknowledgement lifts the recovery barrier

#### Scenario: cancellation arrives after side effects begin

- **WHEN** cancellation is requested after apply has begun modifying repository state
- **THEN** the request records cancellation but continues recovery-critical verification or rollback
- **AND THEN** the terminal state truthfully reports the observed repository condition

#### Scenario: the apply process exits after a side effect

- **WHEN** an operation record remains `Applying` after its owner lease expires
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

Mutation transport SHALL classify caller knowledge as `NotDispatched`, `Completed`, or `Indeterminate`. An `Indeterminate` mutation SHALL NOT be retried or run in-process automatically. An explicit same-operation resubmission SHALL be permitted only after status lookup durably returns authoritative `NotRecorded` with a replay epoch.

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
