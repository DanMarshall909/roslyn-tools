# Critical repository review — making `rt` an excellent tool

- Status: adopted as the critical planning baseline
- Review date: 2026-08-28
- Last reconciled with `main`: 2026-08-30
- Reviewed implementation baseline: `main` at `6817221d3db5b28f94ee72183be0cb0b8bff984e`
- Scope: static and architectural review of CLI, MCP, daemon, workspace loading, analysis/refactoring services, tests, CI, packaging, installers, documentation, and recent hardening commits
- Limitation: this review did not execute the full repository, hostile MSBuild fixtures, filesystem fault injection, packaged MCP clients, or native installers. Passing commit-reported test totals are useful evidence, not independent certification.

## Executive verdict

`rt` has the core of an unusually valuable product: compact, semantic, solution-wide C# operations are substantially more useful to developers and coding agents than repeated text search and raw file reading. The repository demonstrates real engineering discipline through dogfooding, architecture tests, typed-result migration, conservative analysis work, cross-platform intent, and a large automated test suite.

It is not yet a trustworthy stable platform for unattended mutation, arbitrary checkout analysis, or general MCP distribution.

The central issue is not a lack of features. It is that feature breadth and architecture migration have outrun a small set of platform guarantees:

1. **Repository evaluation has no explicit trust boundary.** MSBuild-backed loading can evaluate repository-controlled build logic, but CLI/MCP/daemon are treated as safe long-lived hosts.
2. **Legacy daemon mutation replay remains possible.** Typed recovery protects migrated operations, while non-migrated CLI commands can still time out after dispatch and then run in-process.
3. **Mutation writes are not durable recoverable operations.** The current prepared writer preflights and stages supported changes, backs up originals, and attempts compensating rollback, but its plan, journal, status, and recovery evidence do not survive restart and its path boundary is vulnerable to check/use races.
4. **Workspace completeness is not part of the result contract.** Partial semantic input can still produce proof-like findings and deletion follow-ons.
5. **The MCP host is protocol-incomplete.** It is a handwritten single-message loop with notification, lifecycle, cancellation, schema, structured-result, and version-claim gaps.
6. **Build and release behaviour is not hermetic or credible enough for `1.0.0`.** Debug builds modify the checkout and user profile, while manifests and packages claim a stable version without a corresponding release.

The correct strategy is to stop broadening the command catalogue temporarily and finish one narrow, testable platform spine. A smaller surface that agents can trust will be far more valuable than a larger surface with divergent safety semantics.

## Recommended operating posture now

| Area | Posture on the reviewed baseline |
|---|---|
| Read-only CLI inspection | Useful, but results must be treated as conditional on workspace load quality |
| Broad analyses such as `find-unused` | Advisory only; do not treat high confidence as proof without completeness and closed-world premises |
| Mutation commands | Review/dry-run first; no unattended apply until the mutation kernel and legacy-route containment land |
| Untrusted or newly cloned repositories | Do not run MSBuild-backed semantic operations in a privileged long-lived host |
| MCP host | Experimental integration surface, not a stable protocol implementation |
| Daemon | Transitional transport experiment; no workspace or mutation ownership |
| Portable installers/releases | Pre-release engineering; current stable installation claims are premature |

## What improved on `main` since the first review

Recent commits moved the repository in the right direction:

- **Typed daemon recovery classification** distinguishes completed, unavailable-before-dispatch, and indeterminate-after-dispatch attempts for migrated operations and shares policy across CLI and MCP.
- **`PreparedChangeSet`, the staged writer, and mutation envelopes** create a common vocabulary for Roslyn and filesystem effects, enforce in-memory at-most-once application, stage supported changes, and attempt reverse-order rollback.
- **Host-neutral follow-on actions** reduce renderer-specific suggestion drift.
- **Command-migration architecture ratchets** make it harder to add new CLI/MCP divergence.
- The reported full test suites remain substantial and architecture-oriented.

These changes reduce risk and establish useful seams. They do not close the P0 guarantees:

- operation identity is still generated inside the RPC client rather than caller-known and durable before dispatch;
- legacy daemon routing still has timeout-to-in-process fallback;
- prepared plans have no durable store or identity, status lookup, complete precondition metadata, durable journal, restart reconciliation, or recovery commands; compensating rollback exists only inside the current process;
- workspace completeness and repository trust remain absent;
- MCP and release contracts remain transitional.

## P0 findings

### 1. Untrusted repository evaluation is an unmodelled code-execution boundary

`WorkspaceLoader` creates `MSBuildWorkspace` and opens a supplied `.sln` or `.slnx`. That is necessary for full semantic fidelity, but project evaluation is not equivalent to parsing inert source files. Repository-controlled imports, targets, tasks, analyzers, and generators can cross into executable behaviour depending on the load/build path and configuration.

The current design has no threat model, trust decision, worker boundary, environment allow-list, network policy, or explicit analyzer/generator mode. This is especially serious for an MCP tool because an agent may invoke it automatically on a freshly cloned repository.

**Required correction**

- Model repository trust (`Trusted | Untrusted`) independently from evaluator mode (`NoEvaluation | InProcess | IsolatedWorker`).
- Refuse untrusted MSBuild-backed evaluation until a constrained worker is available.
- Keep untrusted evaluation out of long-lived CLI, MCP, and daemon hosts.
- Treat `NoEvaluation` as code-execution-free, not read-unrestricted: require verified-root handle-relative/no-follow reads or route the operation through the allow-listed worker.
- Pass absolute request context rather than relying on current directory/environment.
- Use an OS-enforced allow-listed filesystem namespace with read-only repository and declared SDK/package inputs, disposable writable outputs/caches/temp, no ambient host profile/credential/device paths, stripped inherited secrets, no network during any untrusted-code phase, and bounded CPU/memory/time; acquire reviewed lockfile-pinned dependencies beforehand through a separate trusted broker that executes no repository code.
- Record trust state/evaluator mode and analyzer/generator policy in provenance.
- Test hostile targets, analyzers/generators, hangs, output floods, source/config symlinks and reparse/mount/hard-link escapes, environment reads, and network attempts.

This should be treated as a product security boundary, not an optional enterprise feature.

### 2. Legacy daemon fallback can still duplicate a mutation

`Program.cs` routes non-daemon and non-typed-daemon-owned commands through `DaemonClient.TrySendAsync`. `DaemonClient` returns `null` after timeout, disconnect, or several pipe failures and the caller then executes the command in-process.

For a legacy mutating command, `null` does not prove the daemon received nothing. The daemon may have applied some or all of the command before the response disappeared. Running it again creates a duplicate-application window.

The newer `DaemonRpcRecoveryCoordinator` correctly treats indeterminate migrated mutations conservatively. That protection does not make the legacy path safe.

**Required correction**

- Immediately remove every apply-capable command from legacy daemon routing.
- Hide apply-capable bridge tools from stable MCP discovery.
- Add an architecture gate preventing any new mutator from entering a bridge or rendered-output route.
- Obtain a bounded repository-scoped operation-epoch token before dispatch, construct `OperationId` from its authenticated monotonic epoch plus a caller nonce, and key intake, status, recovery, acknowledgement, and retention by canonical `(RepositoryId, OperationId)`.
- Compute `RequestHash` from one versioned host-neutral semantic serialization that includes the structured operation ID and canonical public token-claims hash but omits token authentication proof, the hash field itself, replay data, and transport-only metadata; share conformance vectors across adapters.
- Persist an intake receipt before execution enqueue and operation state before side effects; reconcile the gap after lost response or process restart.
- Bound operation epochs, replay duration, terminal bytes, and pinned exceptions; after the replay window, advance a durable monotonic retirement watermark before deleting individual outcomes so every delayed ID is rejected without permanent per-operation state.
- Resolve a genuinely absent receipt under the same intake fence as durable `NotRecorded`, and require its replay epoch for explicit same-operation resubmission before the operation epoch seals.
- Test timeout/disconnect/malformed-response/cancellation before and after intake receipt and ledger creation, including delayed originals, and assert exactly one apply attempt.

### 3. Mutation application is staged and compensating, but not durable

`PreparedChangeSetWriter` now builds and validates an in-memory plan for supported
Roslyn and filesystem changes, stages replacement content, backs up originals,
commits deterministically, ignores cancellation once commit starts, and attempts
reverse-order rollback. If rollback fails it retains a temporary recovery directory
and reports that path. This is a meaningful safety improvement over sequential direct
writes.

The plan and backups remain process-local and are not represented by a durable plan
store, operation ledger, or journal. There is no status lookup, owner lease, restart
reconciliation, deterministic recovery command, complete encoding/file-mode policy,
or handle-relative no-follow boundary. An integer flag prevents invoking one
in-memory object twice; it does not provide durable idempotency across hosts,
retries, crashes, or process restarts.

**Required correction**

- Prepare one immutable plan containing every create/replace/delete/move/project/additional/analyzer-config change.
- Compute plan hash from a versioned host-neutral canonical payload with the hash field omitted, persist the payload and claimed hash in protected storage outside repository control, and require repository ID, plan ID, and plan hash at apply.
- Resolve one OS/service-protected cross-principal/cross-host repository identity and barrier registry; enroll new checkouts only through an explicit authority-fenced operation with continuity proof and a never-reused monotonic identity, then prove no other non-administrative principal or host can write the checkout or require one shared coordination authority; otherwise keep it inspect-only.
- Record expected existence/hash/type, platform identity/link count, encoding/BOM/newlines, permissions/mode, symlink/hard-link policy, repository revision, exact diff, expiry, verification, and recovery data.
- Pin the exact plan and recovery bundle while an operation is nonterminal (including `Recovering`) or `RecoveryRequired`.
- Assign caller-known operation identity and persist its ledger before writes.
- Acquire one exclusive canonical-repository transaction reservation before final whole-plan preflight and hold it through the durable terminal result or recovery handoff so plans cannot interleave.
- Acquire OS-enforced guards for every reviewed dependency/target or use atomic conditional effects so non-`rt` writers cannot win between validation and effect; advisory locks and unconditional replacement are insufficient.
- Journal staging intent first, then stage only under a coordination-domain-protected operation root outside the repository, preferably on the same volume; make crash cleanup idempotent.
- Use the same intake fence when cancellation races receipt-to-ledger promotion, then atomically arbitrate it against a durable `FirstEffectStarted` permit containing the complete effect intent and guard/version evidence; cancellation wins as `CancelledNoEffect` with no-effect proof, artifact inventory, and replay-safe cleanup status.
- Preflight every condition and revalidate the expected intermediate state, remaining plan dependencies, containment, file identity, and link count at every side effect; allow expired-owner reconciliation to return a no-effect stale rejection only when the intact journal proves no `FirstEffectStarted` permit or repository effect was possible.
- Restrict stale rejection to drift found before the first effect; mid-apply drift must stop, journal evidence, and reach verified rollback or `RecoveryRequired`.
- Journal and apply deterministically with atomic per-file replacement where supported.
- Verify postconditions before `Applied`.
- Roll back and verify after failure.
- Enter durable `RecoveryRequired`, retain journal/backups, and publish the barrier through the coordination domain to every writable OS principal before releasing the reservation when rollback cannot restore every path.
- Once the first-effect permit wins, record later cancellation without interrupting verification or rollback.
- Reconcile expired apply-owner leases from durable evidence after restart using a fencing epoch and the same transaction reservation.
- Add explicit fenced recovery operations with append-only `Recovering -> RecoveredApplied | RecoveredRolledBack | RecoveryRequired` transitions.
- Make explicit acknowledgement a non-clean administrative outcome with actor, reason, observed evidence, artifact-retention choice, and immutable audit history.

Do not promise global filesystem atomicity. Promise truthful state, deterministic recovery, and no silent replay.

### 4. Partial workspaces can support unsound proof claims

`WorkspaceLoader` captures diagnostics but only fails when there are no projects and at least one failure diagnostic. Otherwise it can return a solution after partial project load. Workspace warnings are normally suppressed from human output, and the return type does not carry completeness or failed-project identities.

An analysis cannot prove absence of references it was unable to load. This is most dangerous for `find-unused` and `safe-delete`.

**Required correction**

- Declare a semantic-input manifest covering projects, transitive references, target frameworks/configurations, documents, SDK/package/build inputs, and required generator/analyzer stages.
- Return `Complete` only when every required manifest entry is resolved; otherwise return `Partial` when usable scope remains or `Failed` when minimum scope cannot be established.
- Preserve every diagnostic in structured output even when text output suppresses warnings.
- Downgrade all affected findings from partial input.
- Suppress safe-delete and other destructive follow-ons unless completeness and shared confidence policy permit them.
- Make trust, SDK/configuration, source revision, analyzer/generator policy, requested/loaded/failed projects, and limitations visible to agents.

### 5. MCP protocol behaviour is not stable enough to advertise as a product surface

The handwritten MCP server:

- requires every inbound message to contain an ID, so valid notifications are treated as invalid requests and receive responses;
- checks only for the presence of `jsonrpc`, not its value;
- converts malformed JSON into a generic internal error rather than the correct parse-error path;
- accepts Boolean IDs despite the protocol's ID constraints;
- hard-codes one old protocol version and `1.0.0` without real negotiation or a tested compatibility matrix;
- executes a tool while blocking the read loop, so it cannot process cancellation while work is in flight;
- has no request registry, progress model, pagination, bounded concurrency, or protocol lifecycle discipline;
- returns machine JSON as escaped text rather than a true structured result; and
- publishes weak schemas where most complex CLR types degrade to strings and output is merely `object | array`.

These are interoperability defects, not cosmetic SDK preferences.

**Required correction**

- Adopt the official C# MCP SDK unless there is a measured reason not to.
- Otherwise build an equivalent spawned-process conformance suite for every claimed protocol version.
- Correct notification, parse, ID, lifecycle/version, cancellation, and continued-availability behaviour.
- Return schema-valid `structuredContent` plus equivalent text.
- Generate complete schemas including enums, bounds, nullability, nested values, unions, and additional-property policy.
- Keep reading messages while operations run and route cancellation by request ID.
- Bound input, output, item count, time, concurrency, and memory; paginate deterministically.
- Class tools as stable/preview/experimental and remove apply-capable bridges from the stable surface.

## High-impact correctness findings

### `find-unused` silently accepts invalid scope and overstates confidence

`FindUnusedCommand` describes `--scope` as `types | methods | all`, but its help/examples also imply namespace filtering. It passes the raw string to `FindUnusedService`. The service executes candidate loops only for the three recognised strings, so an invalid value can return an empty successful result.

The service contains thoughtful heuristics for xUnit, controllers, extension containers, assembly scanning, and reflection. Those heuristics are useful risk reductions, not proof. High confidence still means only that no supported source reference was found in the loaded solution. Public APIs, external consumers, interface/override dispatch, framework conventions, markup, generators, reflection variants, and incomplete workspace input remain material limitations.

The implementation also performs whole-solution reference search candidate by candidate and does not thread cancellation through the main analysis path.

**Improve it by**

- replacing raw scope strings with validated enums;
- separating symbol-kind selection from namespace/path filtering;
- returning `InvalidInput` on unsupported values;
- making public/external symbols non-proof by default unless a declared closed world is verified;
- attaching evidence and limitations to every finding;
- using shared completeness/confidence policy for suggestions;
- adding cancellation and bounded concurrency; and
- investigating indexed/batched reference analysis with performance budgets.

### The daemon has global-state, privacy, and concurrency debt

`DaemonServer` uses a fixed well-known endpoint and a single-client request loop. Legacy handling can call `Directory.SetCurrentDirectory` without restoring it and uses global exit/console state. It logs the first part of each raw request, which may contain repository paths, source-bearing arguments, or piped content.

Even as a passthrough, it should use explicit request context, per-user collision-resistant endpoint identity, redacted structured logs, input limits, and no process-global semantics. Its single-connection serial model also removes much of the performance rationale for introducing a daemon before workspace caching is safely designed.

### Dry-run output is not yet the reviewed plan contract

A text diff generated from recomputed Roslyn snapshots is helpful but insufficient for a safe apply workflow. An excellent tool lets a user or agent review one stable plan, then applies exactly that plan against explicit preconditions, including from a later process. The plan needs a machine-readable change list, stable ID and hash, and durable coordination-domain-protected storage outside repository control, not only console diff text.

### Error and result contracts are still split by migration state

The repository has good typed `Result<T, RtError>` and `RtResult<T>` direction, but compatibility paths still capture console output, convert exceptions to strings, and reconstruct host-specific behaviour. Do not widen those bridges. Delete them vertically for the five spine commands, including all tests and docs that permit divergent paths.

## Build, packaging, and release findings

### Normal Debug builds have side effects outside build outputs

The CLI project runs post-build targets that:

- format changed files;
- regenerate `docs/COMMANDS.md`; and
- deploy a skill into the user's profile.

That makes an ordinary build non-hermetic, can dirty a checkout, can create cross-worktree races, and can modify user state unexpectedly. Build should compile. Formatting, generation, deployment, dogfooding, and mutation testing should be explicit commands or opt-in targets.

Add a clean-tree/user-profile test around restore/build/test.

### Stable version and installation claims are ahead of reality

The CLI package and MCP manifest claim `1.0.0`, while the repository has no corresponding tag/release artifacts. The README advertises release installers and a global .NET tool. The release workflow builds archives but does not publish the documented NuGet package.

This damages trust precisely where an agent tool needs strong installation confidence.

**Required correction**

- derive all versions from one release source;
- remain pre-1.0 until stable admission gates pass, or explicitly define the compatibility promise represented by 1.0;
- publish the NuGet tool or remove that path from documentation;
- test every supported RID natively through publish, archive, install, launch, fixture operation, MCP handshake, upgrade, rollback, and uninstall;
- retain the prior installed version until health checks pass; and
- preserve an ownership manifest so uninstall never removes unowned files.

### Distribution tests are mostly static presence checks

`scripts/test-distribution.sh` verifies files and a few project properties using `grep`; it does not prove the archive launches or the installers work. The POSIX installer depends on `sha256sum`, which is not a portable macOS assumption. Both installers replace targets before a post-install health check and need stronger interrupted-upgrade recovery.

### CI is Linux-heavy for a filesystem and installer product

The main CI runs on Ubuntu, with a .NET preview compatibility build. That is useful but insufficient for Windows named pipes, PowerShell installers, case-insensitive paths, file locks, read-only attributes, executable bits, symlinks, macOS checksum tooling, and native release archives.

Add Windows/Linux/macOS matrices for the safety kernel, MCP spawn, installer, and filesystem suites.

## Dependency and supply-chain findings

Central package management and warnings-as-errors are strengths. The dependency posture still needs release-grade controls:

- `System.CommandLine` is pinned to an old beta while the project builds a large public CLI surface;
- framework packages are version-skewed against the .NET 10 target;
- no package lock/locked release restore was found;
- no automated transitive vulnerability gate, dependency review, or CodeQL workflow was found; and
- the core project still references command-line infrastructure, while MCP references the CLI executable project.

Upgrade to supported package lines, add locked restore for releases, automate dependency updates/scanning, control action dependencies, remove unused/direct references, and complete the physical host decoupling.

## Testing and engineering quality

### Strengths to preserve

- A large xUnit suite with architecture tests rather than only happy-path unit tests.
- Warnings and code style enforced in build.
- Strong preference for typed application ports and shared result/error contracts.
- Dogfooding and feedback capture.
- Tests around daemon recovery and host parity direction.
- Conservative improvements to reflection/framework blind spots in unused analysis.
- Cross-platform release intent and provenance attestations.

### Gaps to close

- Coverage packages are present, but the main CI does not visibly collect and enforce safety-critical coverage.
- Documentation claims a Stryker gate, but mutation-test configuration/evidence is not an obvious CI contract.
- Fault injection is not yet the central mutation quality gate.
- Spawned MCP/daemon/installer behaviour is under-tested compared with in-process classes.
- Known-defect tests remain skipped, including an extract-method source-call-site case; placeholder skipped tests also exist for an unimplemented add-test command.
- Some debug/regression tests assert implementation artefacts rather than stable public behaviour.
- No hostile repository trust suite exists.

For safety-critical code, measure branch/condition coverage and mutation score at the kernel boundary, but do not chase one repository-wide percentage. A smaller, strongly tested kernel is better than broad superficial coverage.

## Performance and scalability

Performance needs a product contract, not only a future daemon cache.

Current concerns include:

- solution loads are guarded by a static process-wide semaphore;
- the daemon is serial and does not own a safe revision-aware workspace cache;
- `find-unused` can perform whole-solution reference search per candidate;
- many operations lack end-to-end cancellation;
- MCP output has no item/byte paging contract; and
- no stable startup, warm-operation, large-solution, memory, or output-size budgets are enforced.

Measure first. Add representative small/medium/large fixtures and record cold load, repeated operation, project/document count, candidate count, elapsed time, allocations/peak memory, result count, and output bytes. Optimise the canonical operations before adding stateful daemon caching.

Useful likely improvements after measurement:

- one trust-aware workspace session per explicit request scope;
- shared immutable semantic indexes for related read-only operations;
- batched/bounded reference searches;
- deterministic pagination and field projection;
- cancellation at every safe Roslyn and filesystem boundary; and
- command-level cost classifications so agents can avoid unexpectedly expensive operations.

## Product and developer experience

A truly excellent tool should feel predictable before it feels clever.

### Stable workflows, not a flat catalogue

Curate a smaller stable surface around workflows:

1. orient: `prime`, `outline`, `describe`;
2. navigate: references, callers, implementations, source;
3. assess: bounded analysis with evidence/confidence;
4. prepare: immutable reviewed change set;
5. apply/recover: explicit operation identity and status;
6. verify: compile/tests/semantic follow-ons.

Experimental transformations can remain discoverable under preview/experimental maturity without carrying the same stability promise.

### Errors should answer four questions

Every failure should tell the user or agent:

1. what operation failed;
2. what evidence or precondition caused it;
3. what repository state is known now; and
4. the exact safe next action.

For mutations, always include canonical repository ID, operation ID, state, affected paths, journal/recovery status, and copy-pasteable status/recovery commands.

### `doctor` and capabilities should expose reality

Add a machine-readable `rt doctor`/capabilities view showing:

- installed/version/source and available updates;
- supported CLI/MCP protocol versions;
- selected SDK/MSBuild/compiler;
- repository trust state/evaluator mode;
- workspace completeness and diagnostics;
- stable/preview/experimental tool counts;
- resource limits and timeout policy;
- daemon endpoint/status without implying workspace ownership; and
- installer/manifest health.

### One authoritative planning workflow

The repository has useful but overlapping architecture docs, ADRs, task files, Ralph state, issue docs, and OpenSpecs. Reduce duplication. OpenSpec/ADR should own accepted change contracts; GitHub issues/PRs should own execution; generated status should link rather than repeat narrative state in several locations.

## Target architecture

```text
CLI / MCP / future IDE
  -> typed binding + explicit request context
  -> shared application definition/router
  -> typed operation
       -> trust-aware complete/partial/failed workspace acquisition
       -> shared safety/confidence/resource policy
       -> services/Core
       -> mutation prepare/apply/status/recover kernel where applicable
  -> typed result/error/provenance envelope
  -> host projection

Optional daemon:
  transparent typed transport around the operation boundary only
```

Core and application assemblies should not reference CLI/parser or MCP protocol libraries. MCP should not reference the CLI executable. The daemon should not reinterpret semantics. Each stable command should have one operation and one set of contract tests.

## Recommended execution order

### Phase 0 — contain unsafe paths

1. Remove legacy daemon routing from all apply-capable commands.
2. Hide bridge-backed mutators from stable MCP discovery.
3. Force review/dry-run defaults and correct safety claims.
4. Add architecture tests preventing new unsafe bridge routes.
5. Remove normal-build side effects that mutate source or user state.

### Phase 1 — establish trust and mutation truth

1. Write the repository-evaluation threat model with independent trust-state and evaluator-mode policy.
2. Refuse untrusted semantic loading until the worker boundary exists.
3. Add bounded authority-issued operation epochs, caller-known structured operation IDs, canonical repository scoping, and a durable status ledger.
4. Complete prepared-plan metadata and stale-plan preflight.
5. Complete the staged writer with durable plans, journal, verification, restart reconciliation, handle-safe containment, `RecoveryRequired`, and recovery commands.
6. Add hostile workspace and filesystem fault-injection suites across operating systems.

### Phase 2 — complete the five-command spine

Convert and delete all bridges for:

- `outline`;
- `find-refs`;
- `find-unused`;
- `rename`; and
- `safe-delete`.

Require CLI/MCP/direct/passthrough parity, cancellation, completeness, confidence, bounded output, executable examples, and failure injection. Do not fan out to the remaining catalogue until this spine is demonstrably boring and reliable.

### Phase 3 — make MCP a real product surface

1. Adopt the official SDK or equivalent conformance harness.
2. Implement supported-version matrix and protocol-correct lifecycle/notifications/errors.
3. Add concurrent request/cancellation handling.
4. Return real structured content with complete schemas.
5. Add progress, pagination, limits, and maturity classification.
6. Spawn and test the packaged binary, not only in-process server methods.

### Phase 4 — release engineering

1. Make build/test hermetic.
2. Unify version source and return to a truthful pre-release version until gates pass.
3. Upgrade and lock dependencies; add vulnerability/dependency/code scanning.
4. Test native RID archives/installers end to end.
5. Publish the NuGet tool or remove the claim.
6. Add ownership-aware atomic upgrade/rollback/uninstall.
7. Produce evidence-rich release notes.

### Phase 5 — measured excellence

1. Establish startup/operation/memory/output budgets.
2. Optimise canonical read operations and indexes.
3. Add `doctor`, capability introspection, and redacted diagnostic bundles.
4. Curate the stable workflow surface and demote incomplete commands.
5. Reconsider a real workspace daemon only when measured benefit exceeds its isolation/cache/invalidation cost.

## Definition of an excellent stable release

Do not call the tool stable merely because tests pass or every command has a port. A stable release should demonstrate:

- explicit repository trust and safe evaluator behaviour;
- no automatic replay after ambiguous mutation dispatch;
- durable idempotency/status/recovery across process restart;
- truthful direct, recovery-required, and verified post-recovery mutation states, including non-clean `RecoveryAcknowledged`;
- complete/partial/failed workspace provenance;
- no destructive advice from incomplete or unsupported proof premises;
- five-command host parity with bridge deletion;
- MCP conformance, cancellation, structured schemas/results, and resource bounds;
- hermetic builds and clean checkouts;
- native cross-platform installer/release smoke tests;
- one truthful version and installation story;
- locked/scanned dependencies and controlled release provenance; and
- documented performance budgets and residual limitations.

## What not to do

- Do not add more public commands while the platform spine is incomplete.
- Do not build daemon workspace caching now.
- Do not add a generic untyped `execute_command` MCP escape hatch.
- Do not treat more reflection/framework name heuristics as proof of unused code.
- Do not paper over partial mutation with a generic failure status.
- Do not keep a stable `1.0.0` claim merely because changing it is inconvenient.
- Do not let normal builds deploy or rewrite user/repository state.
- Do not optimise around a compatibility bridge that should be deleted.

## Final assessment

The repository does not need a rewrite. Its best parts already point at the right product: typed semantic operations, thin adapters, structured evidence, conservative automation, and dogfooding.

It needs ruthless sequencing.

Finish repository trust, mutation recovery, workspace completeness, and one canonical five-command spine. Replace the handwritten MCP assumptions with conformance evidence. Make builds and releases boring. Then expand.

That path can make `rt` genuinely excellent: not just a broad Roslyn command collection, but a semantic C# operation platform that a developer or coding agent can trust with consequential work.
