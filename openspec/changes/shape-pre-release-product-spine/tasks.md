# Tasks: pre-release product spine

## Execution rules

- This change is a product-shaping programme. It does not authorize overwriting the
  dirty `codex/p0-mutation-containment` branch or declaring its changes verified.
- Finish one representative vertical slice for each new contract before parallel
  conversion. Shared descriptor registries, schemas, DI registration, generated docs,
  and architecture allowlists have one coordinator.
- Use the evidence sequence `baseline -> slice test -> owning project -> solution ->
  packaged journey`. A focused test is not a release gate.
- A checkbox closes only when behaviour, typed failures, cancellation, workspace
  provenance, structured output, documentation, and linked verification agree.
- Platform-hardening tasks referenced below remain authoritative. Do not duplicate or
  weaken them in an implementation PR.
- New standalone public transformations are frozen until Milestone 5 exits.
- The current admission guard limits growth while the pre-freeze surface audit is
  completed. It does not constitute final ratification of the preview catalogue.

## Milestone 0: audit usefulness, then freeze the product surface

### 0.0 Pre-freeze command and workflow analysis

- [x] Inventory all 60 current source-built CLI catalogue entries, measure MCP
  discovery count and bytes under the current profile, and identify ancillary
  primer, suggestion, feedback, daemon, trust, and lifecycle surfaces.
  Distinguish the installed launcher from checkout HEAD.
- [x] Give every current CLI catalogue entry a provisional destination in
  `surface-disposition.md` and record grouped user-job, overlap, correctness,
  and audience reasoning in `pre-freeze-surface-audit.md`.
- [x] Measure representative per-call response bytes using `rt` itself,
  including CLI/MCP and `--llm`; prioritize task usefulness and safety before
  token savings. Full workflow calls and elapsed times remain in the task corpus.
- [x] Cross-check all 60 CLI entries, 60 MCP tools, and 11 ancillary controls
  against advertised effects, trust/workspace, audience, schema, limits, and
  destination in the generated `command-census.jsonl` and individual
  `per-command-evidence-ledger.md`. Trace operation paths for high-risk claims;
  label catalogue, source, live-route, and design evidence separately.
- [x] Execute a task-language and safe-read corpus in roslyn-tools and the
  already-trusted Flo checkout. Record actual suggestion ranks, invocable
  arguments, output usefulness, calls, bytes, elapsed time, Partial and
  unsupported outcomes, and the Flo worktree status attribution limit.
- [x] Revise `surface-disposition.md` after this corpus with one reviewed
  recommended destination for every current command and the control-plane
  routes, ranking usefulness before response cost.
- [ ] Validate mode-level *actual* effects, trust/completeness, request/result
  schema, and failure semantics with focused fixtures; resolve mismatches before
  final ratification of the plan-only preview whitelist. The static evidence
  ledger is not a runtime proof for every variant.
- [ ] Resolve source-audit blockers before admitting any apply-capable CLI mode:
  contradictory `--dry-run`/`--apply` semantics, canonical affected-path trust
  containment for extraction output, authoritative completeness for whole-solution
  edits, and exact revision-bound preview/selection checks. Keep CLI apply
  experimental and optional for the plan-only preview contract.
  - [x] Reject an explicit `--dry-run` combined with `--apply` before invoking
    `add-dependency`, `make-sealed`, or `remove-unused-usings`; keep each
    command's apply-only and preview-only forms valid.
  - [x] Keep the repository's structurally checked diagnostic-only dependency
    validation target in the hashed input set without treating it as an unknown
    compiler mutation; reject added tasks and unsupported target changes.
  - [x] Preflight prepared changes against an authority-derived checkout root
    before staging, including extraction output and all affected path roles.
    This does not close symlink-swap races.
  - [x] Remove the legacy `SolutionWriter` direct apply branch; route
    transformation test fixtures through the prepared writer.
  - [x] Validate prepared affected paths before `SolutionWriter` preview output;
    reject pathless changed or added documents and conflicting affected paths
    without a partial report.
  - [x] Render removed source documents as deletions and reject project
    additions/removals before preview output, matching prepared apply support.
  - [x] Reject identified unsupported non-source document, solution
    analyzer-reference/option, project reference/metadata, and source-document
    metadata/order deltas during prepared change-set construction so preview
    and apply cannot silently ignore them.
  - [x] Exercise the current TypeToProject prepared path with a dry-run that
    preserves source and project-file bytes, an applied source/project/reference
    move, and a sibling-prefix source rejection before any write.
  - [x] Retire the test-only direct-write cross-project move helper and its
    project-file save path after migrating target lookup, namespace rewrite,
    SDK-glob, same-project, and option-validation coverage to the current path.
  - [x] Retire test-only single-type and namespace direct-write move entrypoints;
    prove physical type and namespace file-move preview/apply, collisions, and
    source-folder validation on the prepared path.
  - [ ] Attribute remaining `Partial` inputs precisely, including authored CLI
    tool targets, off-solution build files, and test SDK targets that change
    compiler inputs; prove or scope each before claiming completeness.

### Pre-freeze decision gate

- [ ] Every supported user job has an ordered, executable route or an honest
  unsupported outcome; no internal primitive is suggested as a user workflow.
- [ ] Every catalogue entry and MCP tool has a reviewed destination, with
  distinct typed contracts retained where consolidation would obscure effects.
- [ ] The final disposition explains measured usefulness first and bounded
  output/call cost second, without dropping completeness or safety evidence.

### 0.1 Command census and disposition

- [x] Generate a machine-readable census of the current 60 CLI catalogue
  entries, 60 MCP tools, and 11 known ancillary CLI routes from source-built
  descriptor/help surfaces; verify exact regeneration in the same reviewed
  trust profile with `scripts/generate-command-census.py --check`.
- [ ] Derive or validate the ancillary-route inventory from one authoritative
  registry so new maintainer, feedback, or daemon verbs cannot evade the census.
- [ ] Record for every operation its maturity, effects (including repository-code or
  external execution), default mode, conditional mutability, idempotency, host
  exposure, structured output, trust/workspace requirements, cost, resource limits,
  known limitations, and target disposition.
- [x] Establish the initial `surface-disposition.md` classification against the
  then-current catalogue and fail CI when a public command has no disposition.
  Final ratification remains in the pre-freeze decision gate above.
- [x] Add an architecture gate that rejects a new standalone public command while the
  product-spine freeze is active unless this OpenSpec is amended and approved.

### 0.2 Authority reconciliation

- [ ] Amend or supersede ADR-0002 so workflow suggestions and follow-ons originate in
  host-neutral application metadata rather than adapter-owned response decoration.
- [ ] Amend RT-012's daemon-as-execution-owner decision to acknowledge ADR-0008 and the
  daemon-free `0.1-preview` topology.
- [ ] Reconcile RT-007 completed criteria, its remaining bridge work, and the unchecked
  platform-hardening tasks so one generated status view links to authoritative facts.
- [ ] Add the product-spine OpenSpec and RT-016 to architecture navigation without
  copying their narrative into another roadmap.

### 0.3 Baseline evidence

- [ ] Capture current CLI catalogue, MCP schema, `structuredContent`, suggestion,
  build-side-effect, and packaged-launch evidence as versioned fixtures.
- [ ] Add red tests for the observed discovery count bug, required-query schema
  mismatch, unusable rename MCP schema, missing `structuredContent`, and feedback
  write-safety misclassification.
- [ ] Record existing skipped known-defect tests and assign every stable/preview-
  relevant skip a remove, fix, or explicit experimental disposition.

### Milestone 0 exit

- [ ] Every current capability has one evidence-reviewed final target disposition.
- [ ] No new standalone transformation can enter the public catalogue silently.
- [ ] Documentation authority conflicts are resolved or explicitly tracked.
- [ ] The current baseline failures and dirty implementation state are recorded without
  being mistaken for product-spine regressions.

## Milestone 1: contain unsupported effects and reduce discovery

Depends on the existing `critical-platform-hardening` P0.0 containment slice.

### 1.1 Finish the current containment slice

- [x] Review the existing dirty changes and identify the single safety invariant they
  implement before editing them further.
- [x] Complete review-first defaults and typed safety metadata for every migrated
  mutation without broad mechanical edits that miss variant command shapes.
- [x] Prove timeout, malformed response, disconnect, cancellation, and stale daemon
  cannot cause local fallback or a second mutation execution.
- [x] Run daemon, MCP architecture, command-migration, mutation, formatting, build, and
  full test gates; distinguish baseline failures from regressions.
- [x] Integrate that slice independently before beginning broad product conversion.

### 1.2 Public effect containment

- [x] Remove every apply-capable mutation from normal MCP discovery.
- [x] Make every remaining mutation plan/review-first and remove inconsistent
  apply-default behaviour.
- [x] Hide `probe-di` from normal MCP and mark it repository-code execution in the
  experimental trusted CLI profile.
- [x] Move `feedback_add`, `feedback_list`, `feedback_process`, and `feedback_report`
  to a maintainer/dogfood surface and correct their effect classifications.
- [x] Move the incomplete durable `prepare-operation` primitive to the maintainer
  CLI surface and remove it from public help, suggestions, and MCP discovery.
- [x] Remove trace `debug` from the C# product surface.
- [x] Hide `convert-static-class` until its advertised interface and DI stages exist;
  remove the rt-specific `add-test` mode from public discovery.
- [x] Rename public `safe-delete` language to `delete-unreferenced`/candidate language
  and block planning unless completeness policy permits it.

### 1.3 Daemon-free preview topology

- [x] Define the default preview runtime/package profile in which supported CLI and MCP do not register,
  start, reconnect to, or advertise the daemon.
- [x] Keep daemon lifecycle and performance experiments behind an explicit maintainer
  build/profile.
- [ ] Add parity tests proving daemon absence does not change any supported preview
  request, schema, result, cancellation, or error.

### Milestone 1 exit

- [x] `tools/list` contains no operation capable of applying repository changes or
  executing repository assemblies.
- [x] No supported preview operation depends on daemon availability.
- [ ] Help and schemas distinguish read, plan, repository write, external write, and
  repository-code execution truthfully.
- [x] Incomplete/misleading commands and maintainer plumbing are absent from normal
  public discovery.

## Milestone 2: establish the shared product contract

### 2.1 Descriptor and metadata foundation

- [x] Extend the host-neutral application definition with maturity, effects,
  conditional mutability, idempotency, trust/workspace requirements, prerequisites,
  cost, cancellation, default timeout, item/output limits, limitations, and examples.
- [ ] Generate CLI help, MCP annotations/schema, `doctor`, `suggest`, and documentation
  metadata from the same definition.
- [ ] Normalize request naming and option semantics across CLI and MCP; eliminate mixed
  `dry-run`, `dryRun`, and adapter-specific aliases from canonical contracts.
- [ ] Replace multiple output booleans with one canonical format choice; remove the
  unreleased deprecated `--strict-json` no-op and keep strict JSON canonical.
- [ ] Choose one canonical CLI grammar and remove duplicate root/grouped identities and
  unjustified cryptic aliases before compatibility is promised.

### 2.2 Result-envelope pilot

- [x] Define `rt.result.v1` with outcome, source identity, workspace provenance, data,
  confidence, diagnostics, limitations, next actions, safety, timing, and paging.
- [x] Migrate `outline` as the first complete typed vertical slice through operation,
  CLI, MCP, and any retained internal adapter.
- [x] Return the semantic DTO as CLI JSON and MCP `structuredContent`; keep text as a
  bounded renderer.
- [ ] Add contract snapshots for success, partial, trust-required, ambiguous,
  unsupported, cancelled, timed-out, and bounded/paged outcomes.
- [x] Add an admission test that prevents a stable capability from bypassing the
  shared envelope or emitting structured JSON only inside a text content block.

### 2.3 Workspace provenance and explicit request context

- [ ] Consume the `Complete | Partial | Failed` model from platform hardening rather
  than inventing a second completeness policy.
- [ ] Carry selected repository/solution/project, configuration, TFM, SDK/compiler,
  analyzer/generator policy, trust/evaluator mode, failed inputs, and diagnostics in
  the request/result context.
- [ ] Ensure structured diagnostics are retained regardless of human warning-display
  preferences.
- [ ] Block proof-like findings and plan-only mutations according to the shared
  completeness/confidence policy.

### 2.4 `doctor` vertical slice

- [ ] Define and implement one versioned `doctor` request/result contract spanning
  installed launchers/version, protocol/schema, repository/solution selection,
  SDK/MSBuild/compiler, trust, workspace readiness, capability maturity, and limits.
  The first vertical slice covers launcher/runtime, deterministic solution selection,
  one-shot trust, workspace readiness, maturity counts, and limits; explicit protocol,
  SDK, MSBuild, and compiler identities remain.
- [x] Return deterministic candidates when multiple solutions or projects are viable.
- [ ] Return actionable typed outcomes for missing SDK, trust required, partial load,
  incompatible protocol, damaged installation, and unavailable capability.
- [ ] Add CLI/MCP/package fixtures for a healthy solution, multiple solutions, missing
  SDK, failed project, unapproved repository, and launcher/build-host damage.

### 2.5 MCP foundation

- [ ] Adopt the official MCP C# SDK or complete the equivalent spawned-process
  conformance work already required by platform hardening and RT-012.
- [ ] Fix canonical schemas for `suggest` and `rename`/plan operations before exposing
  them, including optional Git-context query and meaningful typed mutation fields.
- [ ] Add cancellation, progress, timeout, paging, item/byte limits, error, and stdout-
  purity tests against the spawned packaged server.

### Milestone 2 exit

- [ ] `outline` and `doctor` prove identical typed semantics through CLI and MCP.
- [ ] Every public tool has complete generated metadata and a valid structured schema.
- [ ] Workspace trust/completeness is visible and changes allowed conclusions.
- [ ] MCP lifecycle and bounded structured output are verified through a spawned host.

## Milestone 3: make discovery and navigation useful

### 3.1 Shared symbol and solution resolver

- [x] Define stable symbol candidate identities and deterministic ranking for exact
  identity, FQN/signature, exact simple name, filtered suffix, and explicit fuzzy
  search.
- [x] Return kind, full signature, project, namespace, accessibility, generic arity,
  and declaring path for every candidate.
- [ ] Centralize solution selection and symbol resolution for all semantic operations;
  remove command-specific silent selection and opaque not-found ladders.
- [ ] Add ambiguity, overload, generic, nested, partial, test/production duplicate,
  stale identity, and multi-solution fixtures.
- [x] Bound ambiguity responses from the shared symbol-candidate resolver by
  count and UTF-8 bytes while retaining complete listed identities, total and
  truncation metadata, and a filtered `search-symbols` disambiguation route.
  Legacy `SymbolLocator` ambiguity paths remain separate work.

### 3.2 Symbol search

- [ ] Complete `search-symbols` revision-safe paging and output-byte limits; bounded
  first-page search, explicit truncation, and all discovery filters are implemented.
- [x] Migrate `describe` and `source` to consume shared candidate identities while
  retaining separate cost/output contracts.
- [x] Bound `source` text by caller-selected UTF-8 bytes; report total/returned bytes,
  exact declaration ranges, and revision-bound continuation; test truncation, Unicode
  scalar boundaries, stale revisions, and CLI/MCP schema parity.

### 3.3 Relationships

- [ ] Define one bounded relationship graph result for references, callers, callees,
  implementations, implemented members, overrides, overridden members, base, and
  derived types.
- [x] Adapt `find-refs`, `find-callers`, and `find-impl` services behind the canonical
  relationship operation; keep only deliberate thin CLI compatibility wrappers.
- [ ] Add interface/virtual dispatch, overload, extension method, generated source,
  partial workspace, depth, cycle, timeout, and item-limit tests.
- [x] Report reflection, DI, markup, external consumer, and generator limitations.

### 3.4 Workflow-aware suggestions

- [x] Surface `prime`, task `suggest`, and compact `dump-help` from root CLI help;
  keep maintainer controls and the product command catalogue separate.
- [x] Pilot one task-aware implementation-search workflow with a CLI call, an
  MCP call when given an absolute solution, and an explicit partial/truncated-
  result stop condition.
- [ ] Replace isolated name ranking with ordered workflow definitions generated from
  catalogue metadata and shared operation schemas.
- [ ] Support natural-language goal and omitted-query Git-context modes in both CLI and
  MCP schemas.
- [ ] Classify working-tree changes semantically before selecting workflows.
- [ ] Include populated arguments, reason, maturity, effects, trust, cost,
  prerequisites, expected output, dependency, and stop condition for every step.
- [ ] Add a relevance threshold and typed `Unsupported` result naming the missing
  capability.
- [x] Fix total-versus-display count handling and add the observed 37-changed/3-
  displayed regression fixture.
- [x] Fail omitted-query CLI Git-context discovery when changed paths cannot be
  acquired; preserve clean, tracked/untracked, and explicit-query behavior.
- [x] Distinguish an absent path at a valid Git ref from other `git show`
  failures before relying on semantic-diff absence in discovery workflows.
- [x] Reject stable working-tree symlinks, traversal, and unreadable files
  during semantic-diff content acquisition; preserve deletion semantics.
- [x] Close Linux x64 working-tree read races with handle-relative, no-follow
  regular-file access and a bounded FIFO regression.
- [x] Provide equivalent working-tree read containment or honest unsupported
  outcomes on other platforms before declaring complete read effects.
- [x] Make task-aware discovery report working-tree semantic-diff availability
  by platform and preserve ref-vs-ref/two-file routes on unsupported hosts.
- [x] Discover Git-backed semantic-diff working-tree paths from the selected
  baseline, pin refs before path and content reads, and retain root-relative
  literal paths from nested directories.
- [x] Route safely parsed explicit working-tree baselines through task-aware
  CLI/MCP suggestion; keep ambiguous requests unsupported.
- [ ] Add workflow fixtures for understand-symbol, extend-interface, change-DTO,
  investigate-diagnostic, prepare-rename, and working-tree review.

### Milestone 3 exit

- [ ] An agent can begin from an unknown or ambiguous symbol without text search.
- [ ] Relationship results cover the complete declared modes with bounded traversal.
- [ ] `suggest` returns invokable ordered workflows or an honest unsupported outcome.
- [ ] No discovery result executes a recommended step automatically.

## Milestone 4: deliver diagnostics, checks, baselines, and impact

### 4.1 Compiler/analyzer diagnostics

- [ ] Define typed compiler/analyzer diagnostic identities with project/document scope,
  severity, evidence, source revision, and trust policy.
- [ ] Implement current and before/after diagnostic modes with introduced, unchanged,
  and removed classification.
- [ ] Add changed-file/project filters without changing the completeness claim.
- [ ] Add generator/analyzer execution-policy, partial project, duplicate diagnostic,
  and location-drift tests.

### 4.2 Common finding model and `check`

- [ ] Define versioned finding and rule descriptors with stable rule/finding IDs,
  severity, confidence, evidence, locations, limitations, workspace state, baseline
  state, configuration, and suggested actions.
- [ ] Adapt complexity as the first rule pilot and prove CLI/MCP/check-baseline parity.
- [ ] Split `list-smells`, `solid`, circular dependencies, magic strings,
  documentation, and unused candidates into concrete rule IDs.
- [ ] Preserve language features, imports, and static types as descriptive inventory
  results rather than falsely turning them into violations.
- [ ] Keep dependency graph separate from findings and structural search separate from
  ordinary rule execution where their result shapes differ.

### 4.3 Baseline and delta

- [ ] Define a versioned JSON baseline with rule-version and workspace/provenance
  compatibility checks.
- [ ] Implement export and compare modes classifying introduced, unchanged, and
  removed findings.
- [ ] Make finding identity resilient to formatting and harmless line movement.
- [ ] Return typed incompatible-baseline outcomes rather than silently comparing
  different rule or schema versions.
- [ ] Defer direct Git-ref execution until saved-baseline identities are proven.

### 4.4 Change impact

- [ ] Define `impact` input from stable symbol identity, semantic diff, changed paths,
  or prepared plan.
- [ ] Compose semantic changes, relationships, project graph, diagnostic delta, source-
  compatibility analysis, and conservative candidate test projects.
- [ ] Extend relationships with the missing call/type hierarchy edges needed by impact.
- [ ] Explain evidence for every candidate test project and return a suggested
  `dotnet test` command/filter without claiming exact coverage.
- [ ] Add source-level public API classifications for accessibility, signatures,
  nullability, generic constraints, inheritance, and relevant attributes; explicitly
  defer unsupported binary-compatibility conclusions.
- [ ] Add interface change, public DTO change, project-boundary change, generated code,
  reflection/DI, partial workspace, and large bounded graph fixtures.

### Milestone 4 exit

- [ ] Existing debt is distinguishable from findings introduced by the working tree.
- [ ] An interface change reports implementations, callers, projects, diagnostics, API
  risks, and candidate tests with limitations.
- [ ] Partial workspaces cannot produce proof-like unused or deletion conclusions.
- [ ] All analysis output uses bounded typed envelopes and stable identities.

## Milestone 5: establish revision-bound planning and verification

### 5.1 Immutable plan model

- [ ] Define `rt.change-plan.v1` with plan ID, operation, base revision/fingerprint,
  stable input symbol, exact diff, affected paths, expected inputs, diagnostics,
  assumptions, limitations, workspace state, verification actions, and expiry.
- [ ] Bind preview generation and any later apply to the same exact plan bytes; never
  recompute a reviewed plan from command arguments.
- [ ] Store or return plans without writing repository files and protect dirty working
  trees through exact per-input content preconditions.
- [ ] Add missing/expired/incompatible/corrupt/stale plan outcomes.

### 5.2 Plan-only mutation pilots

- [ ] Migrate `rename` as the first prepare-only CLI/MCP vertical slice.
- [ ] Add stale source rejection before any effect and an exact diff/affected-path
  contract.
- [ ] Migrate `change-signature` only after overload, optional/default parameter,
  external API, and call-site fixtures pass.
- [ ] Migrate only the move variants admitted by the final whitelist, one
  representative symbol or slice operation at a time under its completeness and
  transaction preconditions.
- [ ] Reconsider `extract-method`, `extract-interface`, and
  `delete-unreferenced` after the task corpus and safety fixtures; migrate only
  operations explicitly admitted to preview plan-only support.
- [ ] Keep advanced or framework-specific transformations in experimental recipe
  planning; do not widen the preview mutation whitelist opportunistically.

### 5.3 Plan verification

- [ ] Implement `verify-plan` over the planned solution snapshot.
- [ ] Report compiler/analyzer delta, semantic/public API delta, unresolved references
  and call sites, affected projects, candidate tests, workspace state, and limitations.
- [ ] Mark a plan verified only when every required verification completes; otherwise
  return partial/failed evidence without writing.
- [ ] Add introduced compiler error, unresolved generated caller, partial workspace,
  stale plan, cancellation, and resource-bound tests.

### 5.4 Optional experimental direct CLI apply

- [ ] Decide explicitly whether the preview package includes any apply path. Omission
  is acceptable and does not block the supported read/plan product.
- [ ] If retained, restrict apply to explicit direct in-process CLI execution with no
  daemon/MCP, fallback, automatic retry, or ambient plan recomputation.
- [ ] Require exact plan selection, complete workspace where appropriate, revision and
  content preflight, staged writer, truthful limitations, and backup/recovery evidence.
- [ ] Add fault injection for every supported effect boundary and document lack of
  restart-safe recovery until the platform-hardening kernel is complete.

### Milestone 5 exit

- [ ] Planning and verification write no repository files.
- [ ] MCP exposes no mutation apply operation.
- [ ] A stale plan is rejected before any effect and is never recomputed silently.
- [ ] The supported preview remains useful if experimental apply is omitted entirely.

## Milestone 6: consolidate rules and recipes, then retire transitional surface

### 6.1 Rule/inventory migration

- [ ] Move the approved analysis commands behind `check`, `inventory`, structural
  search, or dependency graph according to `surface-disposition.md`.
- [ ] Generate deliberate compatibility warnings only where a preview user journey
  justifies them; otherwise remove unreleased duplicate command names directly.
- [ ] Delete duplicated finding renderers, baselines, confidence ladders, and follow-on
  logic after all callers use the shared model.

### 6.2 Recipe pilot and migration

- [ ] Define versioned built-in recipe metadata using the shared capability and plan
  contracts; do not load third-party recipe assemblies in this change.
- [ ] Migrate one representative framework recipe, preferably `use-time-provider`,
  through list, plan, impact, and verify without apply.
- [ ] Move the remaining approved codemods into recipes, standard Roslyn code actions,
  maintainer-only tools, or removal according to the disposition matrix.
- [ ] Ensure repository-code-executing recipes remain trusted experimental CLI-only.
- [ ] Delete per-command review/dry-run/apply machinery only after every migrated recipe
  uses the shared plan lifecycle.

### 6.3 Public-surface cleanup

- [ ] Remove `debug`, incomplete `convert-static-class`, rt-specific `add-test`,
  deprecated no-op flags, duplicate grammar, and unjustified aliases.
- [ ] Remove ordinary MCP feedback administration and daemon lifecycle tools.
- [ ] Generate the supported capability reference and migration notes from descriptors.
- [ ] Add a manifest test pinning the exact stable/preview/experimental/maintainer and
  host exposure counts for the release candidate.

### Milestone 6 exit

- [ ] No current command is left with an unexplained or accidental disposition.
- [ ] Analyzers share findings; codemods share plans; maintainer plumbing is isolated.
- [ ] The supported public surface matches generated docs and MCP discovery exactly.

## Milestone 7: admit `0.1-preview`

### 7.1 Performance and resource budgets

- [ ] Create representative small, medium, and large solution fixtures.
- [ ] Measure and set cold load, repeated read, elapsed time, peak memory, item count,
  output bytes, cancellation, and timeout budgets.
- [ ] Implement paging or explicit truncation for every unbounded result.
- [ ] Defer caching/daemon work until measurements identify a user-visible bottleneck
  and a revision/trust-safe session design exists.

### 7.2 Hermetic package and version truth

- [ ] Derive CLI, assembly, package, MCP manifest, schema, and release versions from one
  pre-1.0 source.
- [ ] Prove restore/build/test/generation/package do not edit source, user profile,
  launcher configuration, or an installed copy implicitly.
- [ ] Select one supported installation path plus self-contained archives; defer
  installer proliferation and auto-update.
- [ ] Generate and execute every published example against packaged fixtures.

### 7.3 Native acceptance journeys

- [ ] On Windows, Linux, and macOS: install, launch, run `doctor`, run representative
  symbol/impact/check/plan operations, negotiate MCP, inspect tools/schema, cancel a
  request, verify bounds, and uninstall.
- [ ] Test healthy, partial, trust-required, ambiguous, unsupported, stale plan,
  cancellation, timeout, and malformed client requests through the spawned package.
- [ ] Verify a subsequent MCP request succeeds after cancellation or typed failure.
- [ ] Verify no supported preview journey starts a daemon or applies repository changes.

### 7.4 Release evidence

- [ ] Publish release notes stating trusted-local evaluation, plan-only MCP mutations,
  experimental/maintainer exclusions, supported platforms, limits, and known
  limitations.
- [ ] Link package checksums/provenance, native acceptance results, schema versions,
  performance budgets, OpenSpec status, and unresolved deferred work.
- [ ] Keep version below `1.0` and do not claim stable unattended/distributed mutation.

### Milestone 7 exit

- [ ] All BDD scenarios in `specs/product-spine/spec.md` pass against packaged binaries.
- [ ] Existing platform-hardening admission gates required by the selected preview
  surface pass or the corresponding capability remains excluded.
- [ ] The release contains no undocumented host, daemon, trust, mutation, protocol, or
  compatibility promise.

## Explicit post-preview backlog

These are not `0.1-preview` blockers because the preview surface removes their unsafe
paths. They become mandatory before the corresponding later claim:

- [ ] isolated worker evaluation for untrusted repositories;
- [ ] durable prepare/apply/status/recover and cross-process mutation coordination;
- [ ] mutation apply through MCP or daemon;
- [ ] revision-aware workspace sessions/caching after measured need;
- [ ] general third-party rule/recipe extensions and their trust model;
- [ ] binary API compatibility where source compatibility is insufficient;
- [ ] IDE integration, installer proliferation, and automatic update/rollback.
