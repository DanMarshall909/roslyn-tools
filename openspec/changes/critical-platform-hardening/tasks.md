# Tasks: critical platform hardening

The typed RPC recovery coordinator, `PreparedChangeSet`, staged compensating writer,
host-neutral follow-on metadata, and command-migration ratchet on `main` are
foundations. The current writer preflights and stages supported changes, retains
backups during commit, and attempts in-process rollback, but it has no durable plan
store, journal, status ledger, restart reconciliation, or handle-safe symlink
boundary. None of the P0 sections below is complete until its failure, recovery, and
cross-host acceptance tests pass.

## P0.0 containment before further mutation work

- [ ] Disable daemon routing for every apply-capable command that still uses the legacy argument-array protocol.
- [ ] Hide every apply-capable CLI-bridge tool from the stable MCP surface; expose it only as explicitly experimental review/dry-run where safe.
- [ ] Make all remaining mutation commands review/dry-run by default and correct documentation that implies transactional or exactly-once apply.
- [ ] Add an architecture gate that rejects a new apply-capable command without a typed operation, declared safety policy, and mutation-kernel integration.
- [ ] Add regression tests proving a timeout, malformed response, disconnect, or cancelled response cannot cause a second mutation execution.

## P0.1 repository trust and workspace isolation

- [ ] Write a threat model covering MSBuild imports/targets/tasks, analyzers, generators, repository executables, environment inheritance, ambient host filesystem reads, source/log leakage, symlinks, and long-lived hosts.
- [ ] Define canonical repository identity and persisted `Trusted | Untrusted` trust decisions independently from `NoEvaluation | InProcess | IsolatedWorker` evaluator modes, including inspect, grant, revoke, explicit isolation, and non-interactive behaviour.
- [ ] Introduce `WorkspaceRequest` and `WorkspaceProvenance` with absolute context, independent trust state/evaluator mode, configuration, SDK/compiler identity, source revision, diagnostics, and resource budget.
- [ ] Ensure CLI, MCP, and daemon operations do not use or mutate process current directory, global exit code, global console state, or arbitrary inherited environment as request context.
- [ ] Refuse MSBuild-backed semantic loading for untrusted repositories until an isolated worker exists.
- [ ] Implement a constrained evaluator worker with an OS-enforced allow-listed filesystem namespace containing only the read-only checkout, declared read-only SDK/package/tool inputs, and separate disposable writable output, intermediate, cache, and temporary locations; exclude host roots, user profiles, credentials, devices, and undeclared caches; use a secret-free environment allow-list, bounded CPU/memory/time, and network disabled by default.
- [ ] Define analyzer/generator execution modes and prove untrusted code is not loaded into a long-lived privileged host.
- [ ] Add adversarial fixtures containing custom build logic, imported targets, analyzers/generators, symlink escapes, large output, hangs, attempted source/repository-control writes, and attempted host-file/secret/network access; prove denied writes leave the checkout byte-identical and undeclared host paths are unreadable.

## P0.2 durable operation identity and transport safety

- [ ] Add caller-generated `OperationId` to every apply request before dispatch; key every receipt, ledger, status, recovery, acknowledgement, and retention lookup by canonical `(RepositoryId, OperationId)`, and keep request-attempt correlation separate.
- [ ] Define an OS/service-protected cross-principal and cross-host registry outside repository control that binds checkout/Git-common-dir filesystem identity to one non-reused repository ID and records mode, owner/authority, active recovery barrier, and retirement; never recreate missing/inconsistent identity state or retain less than an active barrier, allow machine-local state only when other hosts cannot write, combine it with an `ExclusivePrincipal | SharedAuthority` mutation coordination domain, and return `RejectedUnsafeCoordination` when ownership or shared-authority proof is unavailable.
- [ ] Implement a durable intake receipt and operation ledger with append-only transition history and `Received | NotRecorded | Prepared | Applying | Reconciling | Applied | RolledBack | RecoveryRequired | Recovering | RecoveredApplied | RecoveredRolledBack | RecoveryAcknowledged | CancelledNoEffect | RejectedStale | RejectedConflict | RejectedUnsafeCoordination | Expired` states.
- [ ] Record owner instance, monotonically increasing fencing epoch, lease expiry, heartbeat, and transaction-reservation identity for `Applying` and `Recovering`; acquire one OS-enforced exclusive reservation per canonical repository before final preflight, hold it through terminal completion/recovery handoff, and require it for expired-owner takeover.
- [ ] Define one versioned host-neutral SHA-256 request canonicalizer that includes every semantic apply field but omits `RequestHash` itself, replay epoch, transport attempt/correlation metadata, cancellation state, and authentication material; use shared cross-adapter test vectors so identical operation-ID replay returns the existing receipt/result and differing replay returns `OperationConflict`.
- [ ] Before execution enqueue, acquire a cross-process, store-enforced intake fence keyed by repository and operation ID and persist `Received(RepositoryId, OperationId, RequestHash, CanonicalRequest)` outside daemon logs; reconcile receipts without operation records on startup/status lookup.
- [ ] Make `GetOperation` require repository ID and create authoritative `NotRecorded(RepositoryId, OperationId, RequestHash, ReplayEpoch)` only while holding that fence after proving no intake remains in flight; reject delayed attempts without the epoch and permit one explicit exact-request transition to `Received`.
- [ ] Atomically create a compact idempotency tombstone for every terminal/expired operation; preserve identity, request/plan hashes, canonical outcome, evidence digest, and cleanup/recovery summary for the repository identity's lifetime, check it under the intake fence, and allow compaction only after a permanent retired-repository marker makes an old never-reused repository ID unconditionally rejectable.
- [ ] Preserve composite repository/operation identity and status semantics across CLI, MCP, in-process, and daemon adapters.
- [ ] Retain `NotDispatched | Completed | Indeterminate` as transport knowledge, prohibit automatic mutation retry/fallback after `Indeterminate`, and allow explicit same-operation replay only after durable `NotRecorded` resolution.
- [ ] Test lost first response, process termination before/after intake receipt and operation-ledger write, a status/intake race, cancellation racing receipt-to-ledger promotion, a delayed original after `NotRecorded`, lost replay authorization, tombstone compaction and delayed identical/different submissions, repository-identity retirement, the same operation ID in two repositories, two OS principals and two hosts sharing one writable checkout with and without an authority, ownership transfer, every journal boundary, owner-lease expiry, a stale owner resumed before/after every reservation/epoch check, concurrent reconciliation, overlapping plans for the same and different repositories, malformed response, duplicate request, concurrent duplicate request, stale daemon, pre-effect cancellation at every wait/staging/preflight boundary, the cancellation-versus-first-effect race, cancellation replay, cleanup restart, and post-effect client cancellation.

## P0.3 complete prepared-change-set model

- [ ] Extend `PreparedChangeSet` with plan ID, repository identity/revision, expiry, canonical operation list, exact diff, diagnostics, assumptions, confidence, verification, operation-scoped outside-repository staging/backup roots, cleanup/recovery policy, and per-path external-write guard or conditional-effect requirements.
- [ ] Add a protected durable plan store outside the target repository, atomically keyed by repository ID, plan ID, and plan hash, user-private in proven exclusive-principal mode or authority-owned/access-controlled in shared mode, with schema versioning, bounded retention for unreferenced plans, durable ledger-reference pinning, and crash-safe cleanup.
- [ ] Pin exact plan bytes and their recovery bundle while any associated operation is `Received`, `NotRecorded`, nonterminal (including `Recovering`), `RecoveryRequired`, or `CancelledNoEffect` with pending control-artifact cleanup; release only after verified terminal cleanup, durable expiry, or explicit acknowledgement, with garbage-collection tests across retention expiry and restart.
- [ ] Reject missing, expired, unreadable, schema-incompatible, or hash-mismatched plans before entering `Applying`; prove review and apply can occur in separate processes without recomputation.
- [ ] Model creates, replacements, deletes, moves, project edits, normal Roslyn documents, additional documents, and analyzer-config documents.
- [ ] Record expected existence/hash/type for every affected path and expected post-apply hashes.
- [ ] Preserve or explicitly change encoding, BOM, newline, file mode/permissions, and executable bit.
- [ ] Define case-sensitivity, long-path, read-only, and symlink policy across supported operating systems; for hard links, record platform file identity, link count, and known aliases, require reviewed `Reject | Preserve | BreakByReplacement` semantics, and block unattended apply when shared-inode effects cannot be proven safe.
- [ ] Enforce separate canonical roots: source/destination and their symlink-resolved paths stay under the repository, while staging/backup/control artifacts stay under a coordination-domain-protected operation root outside and non-overlapping with the repository; require a safe same-volume control root when the effect primitive needs one.
- [ ] Generate preview and apply from the same immutable plan; apply must not recompute from command arguments.
- [ ] Reject pathless, duplicate, conflicting, non-verifiable, expired, or drifted plans before writes.

## P0.4 staged apply, rollback, and recovery

- [ ] Extend the current staged compensating writer into a general filesystem writer with atomic per-path replacement where supported, deterministic ordering, and durable journal/backups that survive process restart; durably journal staging intent before writing any outside-repository staging artifact and make cleanup idempotent.
- [ ] Before final preflight, prove and deterministically acquire an OS-enforced external-write guard set for every reviewed dependency, affected object, and directory entry, or equivalent atomic conditional-effect tokens; hold it with the repository reservation through durable outcome and block unattended apply when advisory/check-then-act primitives cannot exclude non-cooperating writers.
- [ ] Under both boundaries, preflight all revisions, read dependencies, hashes, existence, permissions, destinations, symlinks, hard-link identity/count, path boundaries, and required free space; before each guarded write/move/delete/restore, revalidate the guards/tokens, expected intermediate state, prior effects, every still-relevant dependency, ledger state, fencing epoch, lease, path, file identity, and link count.
- [ ] Return `RejectedStale` only for drift detected before the first effect; after any journalled effect, stop the remaining plan and verify rollback to `RolledBack` or `RecoveryRequired`.
- [ ] Verify expected postconditions before recording `Applied`.
- [ ] Implement rollback and verification for every supported operation type.
- [ ] Add durable `RecoveryRequired` intervention state, retained journal/backups, affected-path state, deterministic recovery actions, and a coordination-authority recovery barrier visible to every writable OS principal before the transaction reservation is released.
- [ ] Before the first repository effect, atomically arbitrate durable cancellation against a `FirstEffectStarted` permit containing the complete effect intent, expected intermediate state, and guard/version evidence; record `CancelledNoEffect` with no-effect proof, artifact inventory, and cleanup status when cancellation wins, make exact replay return that outcome, and reconcile both an uncertain first effect and idempotent outside-repository cleanup after restart.
- [ ] Make recovery-critical rollback non-interruptible after side effects begin while recording the caller's cancellation request.
- [ ] Add `rt recover status|resume|rollback|acknowledge --repository <path-or-id> <operation-id>` or equivalent host-neutral operations; resolve the canonical composite identity, append `Recovering -> RecoveredApplied | RecoveredRolledBack | RecoveryRequired` transitions, and make acknowledgement record actor/reason/evidence without claiming cleanliness.
- [ ] Reconcile expired `Applying` and `Recovering` records on startup and status lookup; apply bounded mutation-worker shutdown, acquire the same exclusive transaction reservation before advancing the epoch, re-establish complete external-write guard/conditional-effect protection before any repository effect, and allow one owner to prove completion, resume or roll back from a declared journal boundary, or enter `RecoveryRequired`; if reservation release or external protection cannot be proven, revoke the epoch, retain the possibly in-flight boundary, and perform no reconciliation write.
- [ ] Add adversarial non-`rt` writer processes and fault injection before guard acquisition, between validation and effect, and between every operation boundary: stage/write/flush/replace/move/delete/permission/verification/journal/rollback/recovery/acknowledgement/cleanup/process termination, including guard loss, unconditional-replace rejection, stale-versus-rollback outcomes, artifact retention, and append-only-history assertions.
- [ ] Test encoding/BOM/newlines, executable bits, locked/read-only files, disk-full simulation, case-only moves, symlinks, in-root and out-of-root hard-link aliases, link-count drift, cross-volume moves, and rollback failure on Windows, Linux, and macOS.

## P0.5 workspace completeness and proof policy

- [ ] Replace tuple workspace results with `Complete | Partial | Failed` plus a declared semantic-input manifest covering projects/transitive references, target frameworks/configurations/compilation contexts, documents, references, SDK/package/build inputs, generated output, and operation-required evaluator/analyzer/generator stages.
- [ ] Record per-input loaded/resolved/failed/policy-blocked/excluded state and classify every diagnostic by completeness impact; keep the full manifest and diagnostics in structured provenance even when a text renderer suppresses warnings.
- [ ] Define `Complete` only when every required manifest input is available at the declared revision and every required stage succeeds; allow exclusions only under a validated operation-specific irrelevance premise, return `Partial` when usable scope remains, and `Failed` when minimum scope cannot be established.
- [ ] Make partial/failed state flow through every operation and result envelope.
- [ ] Block proof confidence, safe-delete advice, and automatic mutation suggestions from partial workspaces.
- [ ] Add a shared `Confidence { level, basis, evidence, limitations }` model and policy service.
- [ ] Define the closed-world premises required before a publicly or externally visible symbol can be classified as proven unused.
- [ ] Cover interface/override dispatch, extension use, reflection, DI/framework activation, serialization, route binding, markup, source generation, external consumers, and unsupported mechanisms.
- [ ] Add partial-project, missing target-framework/configuration, failed project/metadata/framework reference, missing SDK/package/import, generator/analyzer-disabled or failed, narrowed-scope, completeness-impacting diagnostic, and external-public-API fixtures.

## P1.1 five-command canonical spine

- [ ] Convert `outline`, `find-refs`, `find-unused`, `rename`, and `safe-delete` to one typed request/operation/result/provenance contract.
- [ ] Delete CLI reconstruction, rendered-output parsing, direct service shortcuts, and compatibility bridges for each converted command.
- [ ] Prove CLI, MCP, in-process, and transparent-daemon parity for success, expected error, cancellation, partial workspace, and bounded-output cases.
- [ ] Require absolute request context and the same safety/confidence/resource policy in every host.
- [ ] Do not fan out broad command migration until all five pass the admission matrix.

## P1.2 `find-unused` correctness and performance

- [ ] Replace string scope with a validated enum; split symbol-kind filtering from namespace/path filtering.
- [ ] Return `InvalidInput` for unsupported values instead of an empty successful result.
- [ ] Make externally visible/public symbols non-proof by default unless a declared closed-world policy is satisfied.
- [ ] Include evidence and limitations for every finding and suppress safe-delete follow-ons unless shared policy permits them.
- [ ] Thread cancellation through syntax, semantic-model, and reference searches.
- [ ] Replace candidate-by-candidate whole-solution scanning where possible with indexed/batched analysis and bounded concurrency.
- [ ] Add performance fixtures and budgets for small, medium, and large solutions; record candidate count, projects, documents, elapsed time, and peak memory.

## P1.3 MCP conformance and structured contract

- [ ] Adopt the official C# MCP SDK, or document why not and build an equivalent spawned-process conformance suite.
- [ ] Define the supported protocol-version matrix and remove hard-coded version claims not exercised in CI.
- [ ] Correct JSON-RPC parse/invalid-request handling, request IDs, notification no-response behaviour, and lifecycle/version negotiation.
- [ ] Keep reading the protocol stream while operations execute so cancellation and other allowed messages can be processed.
- [ ] Implement per-request cancellation, long-operation progress, deterministic paging, and bounded concurrency.
- [ ] Return schema-valid `structuredContent` for structured tools plus an equivalent text view.
- [ ] Generate complete JSON Schema for enums, bounds, nullability, arrays, nested objects, unions, and `additionalProperties` policy.
- [ ] Enforce request/output/item/time/memory limits and explicit continuation/limitation metadata.
- [ ] Classify tools as stable/preview/experimental and hide bridge-backed mutations from stable discovery.
- [ ] Add spawned tests for malformed JSON, notifications, unsupported versions, cancellation, large payloads, client disconnects, schema validation, and server recovery.

## P1.4 capability catalogue and executable documentation

- [ ] Generate or verify names, categories, aliases, defaults, validation, safety, formats, maturity, request/result types, examples, primer text, and follow-on actions from one catalogue.
- [ ] Remove command-local known-option objects and raw parser access after shared binders exist.
- [ ] Execute every published CLI and MCP example against deterministic fixtures in CI.
- [ ] Add architecture tests that reject drift between CLI help, MCP schema, server manifest, `prime`, and generated command documentation.
- [ ] Make known limitations and confidence/resource constraints machine-readable.

## P1.5 daemon passthrough boundary

- [ ] Mark workspace caching/session ownership as parked and superseded for this phase in all active architecture documents.
- [ ] Remove legacy CLI argument-array routing for mutations and eventually for every converted spine command.
- [ ] Use a per-user, collision-resistant, access-controlled endpoint rather than one global well-known pipe/socket name.
- [ ] Remove request use of process current directory, global environment/exit code, and console capture.
- [ ] Replace raw request/piped-input logging with bounded structured, redacted diagnostics and retention controls.
- [ ] Add tests proving no cache, no operation ownership, no mutation retry, no global-state leakage, and exact observable parity.
- [ ] Require a separate approved OpenSpec change and ADR before daemon workspace caching or semantic ownership is proposed.

## P1.6 hermetic build and developer workflow

- [ ] Remove formatting, generated-document writes, and user-profile skill deployment from normal Debug build targets.
- [ ] Provide explicit `format`, `generate-docs`, `deploy-skill`, `dogfood`, and `mutation-test` scripts/targets with predictable inputs and outputs.
- [ ] Add a clean-tree build test that fails if restore/build/test modifies the checkout or user profile.
- [ ] Make generated files reproducible and verify them in CI without rewriting them.
- [ ] Remove committed debug logs and move durable project status from overlapping `tasks/`, `ralph/`, issues, and OpenSpecs into one authoritative workflow.

## P1.7 dependency and supply-chain hygiene

- [ ] Upgrade from the historical System.CommandLine beta to a supported stable release and update shared binder/parser tests.
- [ ] Align Microsoft.Extensions/System.Text.Json package versions with the target framework or document intentional version skew.
- [ ] Add lock files and locked restore for release builds.
- [ ] Add automated dependency updates, transitive vulnerability scanning, dependency review, and CodeQL/static security analysis.
- [ ] Pin or policy-control GitHub Actions and document the trusted release action chain.
- [ ] Remove unused direct package references and keep host/protocol dependencies out of the core project.

## P1.8 CI, distribution, and release admission

- [ ] Run build, tests, spawned CLI/MCP checks, and filesystem-sensitive suites on Windows, Linux, and macOS.
- [ ] Collect and enforce meaningful coverage for the safety kernel and protocol boundary; make mutation-test scope and thresholds executable rather than documentary.
- [ ] Replace static grep-only distribution checks with native publish/install/launch/fixture/upgrade/rollback/uninstall smoke tests for every supported RID.
- [ ] Make POSIX installer checksum verification portable to macOS and preserve an ownership manifest for safe uninstall.
- [ ] Make install/upgrade switch versions atomically and retain the previous version until health checks pass.
- [ ] Derive assembly, NuGet, MCP manifest, archive, and release versions from one source.
- [ ] Keep the product pre-1.0 until stable admission gates pass, or explicitly document the compatibility policy represented by `1.0.0`.
- [ ] Publish the documented NuGet tool in the release workflow or remove the NuGet installation claim.
- [ ] Verify checksums, provenance attestations, server manifest, archive contents, and native execution before attaching release artifacts.
- [ ] Document verification evidence and residual risks in release notes.

## P2 product excellence after the safety spine

- [ ] Define measured startup, warm-operation, large-solution, output-size, and memory budgets; benchmark regressions in CI where stable.
- [ ] Add a capabilities/doctor command that reports SDK selection, trust state, evaluator mode/policy, workspace completeness, protocol versions, limits, and installation health.
- [ ] Add deterministic diagnostic bundles with explicit user consent and source/secret redaction; no telemetry by default.
- [ ] Curate a smaller stable command surface around complete workflows rather than exposing every experimental transformation equally.
- [ ] Improve errors with structured causes, evidence, recovery commands, and copy-pasteable next actions.
- [ ] Reconsider a revision-aware workspace daemon only after P0/P1 gates are complete and measured cold-start cost justifies its complexity.
