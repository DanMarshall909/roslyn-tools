---
tags:
  - architecture
  - product-surface
  - mcp
  - developer-experience
ticket: rt-016
severity: High
status: Open
area: architecture
owner: ""
reported: 2026-08-31
summary: "Shape a trustworthy 0.1-preview around semantic discovery, impact, plan-only changes, and verification instead of exposing every rt experiment as an equal product promise"
---

# Shape the pre-release product spine

## Context

At the 2026-10-01 pre-freeze audit baseline, the source-built catalogue exposed
60 entries: 25
inspect/analyze/CI, 34 refactor (including the local `trust` control), and one
lifecycle command. Thirty-nine entries did not advertise JSON output even though
several could emit JSON. The MCP surface under that persistent-trust profile
exposed 60 tools: 58 shared with CLI plus `prime` and `suggest_command`.
The breadth is concentrated in bespoke refactors even though the
beginning and end of the agent workflow remain incomplete:

```text
orient -> discover -> understand -> assess impact -> plan -> verify
```

The pre-freeze audit found several misleading or incomplete surface contracts:

- `suggest` misses parameter-edit and dependency-boundary workflows, sometimes
  ranking an internal coordination primitive as if it were a refactor plan;
- a healthy checkout can report `Partial` workspace state, weakening absence and
  safe-delete conclusions;
- mutation variants, including batch rename, signature changes, and project
  moves, need distinct plan evidence before preview admission;
- contradictory dry-run/apply flags could still write for three commands, and two
  extraction output paths lack trusted-root containment at the common writer;
- MCP output schemas are generic and some request properties are undocumented;
- `convert-static-class`, the mixed `add-test` mode, feedback commands, daemon
  controls, and `probe-di` are now outside normal public discovery, but their
  internal or experimental contracts still need explicit review.

The existing `critical-platform-hardening` OpenSpec already owns repository trust,
workspace completeness, mutation containment and recovery, canonical operation
parity, MCP conformance, resource bounds, and release engineering. Adding product
surface work to its 134-task programme would obscure both scopes.

The first source-audit safety repair rejects an explicit `--dry-run` together
with `--apply` for `add-dependency`, `make-sealed`, and `remove-unused-usings`
at argument parsing. Apply-only and preview-only forms remain valid. The other
apply admission blockers in the OpenSpec remain open.

The workspace input fingerprint now recognizes the repository's structurally
checked diagnostic-only dependency validation target and still hashes it. A
fixture with the target reports `Complete`; adding an unrecognized task remains
nonreusable. The full roslyn-tools checkout still reports `Partial`: authored
CLI tool targets, off-solution build inputs, and a test SDK target with compiler
input effects need separate proof before a complete claim. Input-set failure
detail now gives a bounded list of known non-reusable build files, prioritizing
declared projects and retaining the `Partial` state (ADR-0081).
Normal Debug builds no longer run CLI formatting, command-reference, or
user-profile skill deployment hooks (ADR-0093). This removes observed build
effects, not the need to prove every remaining input before upgrading `Partial`.
Linux CI now checks separate plain restore, Debug build, and test phases against
authored checkout files and a disposable profile (ADR-0094). The gate is build
side-effect evidence; it does not prove the full workspace input set complete.

## Decision

Create one dependent OpenSpec change:

- [shape-pre-release-product-spine](../../../../openspec/changes/shape-pre-release-product-spine/proposal.md)

It defines the exact `0.1-preview` maturity tiers, command disposition, shared
finding and recipe models, workflow-aware discovery, diagnostics, change impact,
revision-bound planning, and verification. Platform guarantees remain owned by
`critical-platform-hardening` and are referenced as admission gates.

## Acceptance criteria

- [x] Before final surface freeze, review the live CLI/MCP catalogue against
      representative user and agent jobs. Record every command's usefulness,
      overlap, safety, audience, and disposition, then measure calls/output cost
      without sacrificing completeness evidence. The interim command-admission
      guard is not final ratification; see the
      [pre-freeze audit](../../../../openspec/changes/shape-pre-release-product-spine/pre-freeze-surface-audit.md),
      [per-command ledger](../../../../openspec/changes/shape-pre-release-product-spine/per-command-evidence-ledger.md),
      and [generated census](../../../../openspec/changes/shape-pre-release-product-spine/command-census.jsonl).
- [x] Every current command has one recorded `Stable candidate | Preview |
      Experimental recipe | Maintainer-only | Remove | Consolidate` disposition.
- [ ] New standalone command registration is frozen until the product spine is
      implemented and the command-admission gate requires maturity, safety,
      prerequisites, cost, workspace, and output metadata.
- [ ] The stable preview surface is read-only; MCP mutations are plan-only and no
      MCP tool can apply repository changes.
- [ ] The preview package does not depend on or advertise the daemon.
- [ ] `doctor`, symbol search, relationships, diagnostics, `impact`, `check`,
      baseline comparison, workflow suggestions, revision-bound plans, and plan
      verification have versioned CLI/MCP contracts and BDD coverage.
- [ ] Analysis commands use common finding identities and envelopes; framework-
      specific codemods use experimental recipes rather than permanent top-level
      stable commands.
- [ ] CLI JSON and MCP `structuredContent` project the same semantic result for
      every supported preview operation.
- [ ] Existing platform-hardening, RT-007, and RT-012 gates are linked rather than
      copied into a second backlog.
- [ ] `0.1-preview` package tests pass on each supported operating system and state
      the trusted-local, plan-only scope truthfully.

## Implementation progress

- Doctor result safety now includes `RepositoryCodeExecution` after trusted
  workspace acquisition begins, even if acquisition fails; installation-only,
  trust-required, and SDK-bootstrap failure results omit it. Mode-specific
  discovery metadata remains open.
- CLI output-format flags now fail early when a selected public command's
  declared formats do not include the requested effective format (ADR-0099).
  Source-built `complexity --llm` had exited 0 with no output; grouped, root,
  alias, and second-command subprocess cases now require a named exit-1 error
  before workspace loading. Undeclared refactor formats remain to inventory.
- Standalone `complexity --top 5 --json` now scopes full-value rollups to the
  returned methods and reports scope/total counts (ADR-0098). On a source-built
  run, stdout fell from 288,729 to 3,289 bytes with five methods, four type
  rollups, and one namespace rollup. This is a per-call byte measurement;
  complexity's `check` rule and broader finding-model migration remain open.
- Shared `SymbolCandidateService` ambiguity now reports bounded complete
  identities, total/displayed counts, truncation, and a filtered
  `search-symbols` route (ADR-0095). A source-built `relationships callers
  ExecuteAsync` error fell from 85,921 to 4,150 JSON stdout bytes for 130
  matches. The general no-store legacy `SymbolLocator` ambiguity path is also
  bounded (ADR-0096), as is member-signature overload ambiguity (ADR-0097).
  Wider search paging remains open.
- Root CLI help now shows invocable `prime`, `suggest`, and compact `dump-help`
  entry points. `dump-help --help` exposes its own options. The generated
  catalogue remains at 59 product commands and was regenerated with no diff;
  maintainer controls remain outside public discovery.
- The durable `prepare-operation` primitive now has an explicit maintainer CLI
  route. Public help, task suggestions, command reference, site catalogue, and MCP
  discovery exclude it until a complete plan lifecycle is available (ADR-0082).
- CLI and MCP suggestions now report `Unsupported` with a named missing
  capability for plan preparation and verification requests (ADR-0083). General
  workflow ranking, relevance thresholds, and Git-context parity remain open.
  For the captured `Verify a prepared plan without applying` query, one
  source-built CLI response fell from 2,452 to 103 stdout bytes; this is a
  per-call byte comparison, not a complete workflow or token budget.
- The first task-level suggestion pilot routes "find every implementation of
  IRtCommand" to a populated read-only `relationships implementations` CLI
  call. MCP gets an invokable call only with an absolute solution path; without
  one the response names that prerequisite. The response also names the
  `Partial`/truncation stop condition;
  it does not claim exhaustive evidence from the current repository workspace.
  On that captured query, the source-built CLI JSON response changed from
  2,774 bytes of irrelevant ranked commands to 775 bytes of one workflow;
  the compact text route was 341 bytes. These are per-call stdout sizes, not
  measured end-to-end workflow savings. General task routing remains open.
- Omitted-query CLI suggestion now counts every changed C# path before applying
  `--top` to displayed outline calls. A temporary-Git regression covers 37
  changed C# files, three displayed calls, a non-C# change, and `--top 0`.
  Git acquisition now fails explicitly for non-repositories, unborn `HEAD`,
  failed changed-path commands, and process startup errors instead of claiming
  a clean tree. The host Git adapter drains both output pipes and bounds error
  text. Queried suggestions remain Git-independent (ADR-0086).
  Semantic diff now proves literal tree membership before treating a path as
  absent, and returns a typed external-tool error for failed content reads
  (ADR-0087). Semantic Git-context classification and MCP omitted-query parity
  remain open.
  Working-tree content preflight now rejects symlink traversal and access
  failures instead of reading outside content or claiming deletion (ADR-0088).
  Linux x64 now uses handle-relative no-follow reads and regular-file checks;
  other platforms now return an explicit unsupported result for Git-backed
  working-tree content while ref-vs-ref remains available across hosts and
  direct two-file comparison remains available in the local CLI (ADR-0089).
  MCP now returns typed `Unsupported` for two-file requests, including in a
  trusted host, because the direct file provider lacks the required contained
  reader (ADR-0100). Git-mode MCP now checks the selected directory against
  the active trust scope even under startup-scoped trust, before Git reads;
  a verified-root file request and contained reader remain open.
  Direct daemon RPC now rejects every `InProcess` port before parsing or trust/SDK
  admission, closing a separate trusted-solution bypass of MCP file and Git
  mode guards. Eligible daemon-preferred read-only ports retain typed execution.
  Explicit C# working-tree review now routes through
  the shared suggestion service to one semantic-diff step on Linux x64, with
  CLI/MCP arguments when a target is known. Other hosts receive a typed
  unsupported outcome and cross-platform alternatives (ADR-0090). Broader
  Git-context classification and workflow routing remain open. The workflow
  distinguishes HEAD-versus-current-files from unstaged-only requests and
  distinguishes unambiguous non-HEAD working-tree baselines from ambiguous
  requests. Semantic diff now enumerates
  tracked paths directly against the pinned `--from` commit and adds untracked
  files, so direct `--from` comparison is complete for the observed working
  tree (ADR-0091). The suggestion route now emits matching CLI `--from` and
  MCP `from` arguments for one safely parsed ref; conversational, partial, or
  conflicting refs remain typed unsupported (ADR-0092). For the captured
  `review my working tree C# changes` query with no solution argument,
  source-built suggestion output fell from 2,417 to 625 JSON stdout bytes
  and from 995 to 360 text stdout bytes. This measures one response, not an
  end-to-end workflow or token budget.
- Explicit configured project/layer violation requests now suggest one
  `check-architecture` read step using `.rt/architecture.json`, with populated
  MCP arguments when the solution path is absolute. Inventory, custom-config,
  SOLID, baseline, and mutation requests retain their prior routes; wholesale
  non-Microsoft core dependency extraction remains named `Unsupported`.
  The captured layering query fell from 2,530 to 684 JSON stdout bytes.
  The check still lacks result-level workspace completeness evidence, so its
  suggestion stops short of treating zero violations as whole-solution proof.
- Explicit proposed symbol/project change-impact requests now return named
  `Unsupported` from CLI and MCP instead of a `move` recommendation. The
  response names manual reference/project-dependency, build, and test steps and
  does not infer safety from missing source edges. A source-built CycleEngine
  suggestion fell from 2,705 to 409 JSON stdout bytes; the impact operation
  and end-to-end workflow remain open.
- Explicit named-symbol reference requests now suggest one populated
  `relationships references` read step in CLI and MCP (MCP needs an absolute
  solution path). Supported `type`, `method`, `field`, `property`, and `event`
  kind cues are preserved; constructor, namespace, parameter, member, and
  specific type-subtype cues retain general discovery. The source-built
  WorkspaceLoader suggestion emitted 881 JSON stdout bytes. The exact CLI step
  returned `Partial`, 100 of 289 references,
  truncated with no continuation token and 60,774 JSON stdout bytes, so the
  workflow does not claim an exhaustive result. Requests with unsupported
  scope or option qualifiers remain on general discovery rather than receiving
  an unqualified call. General workflow routing and revision-safe relationship
  paging remain open.
- Explicit method staticness requests now preserve the requested direction in
  CLI and MCP `suggest`: instance-to-static recommends only `make-static`, and
  static-to-instance/non-static recommends only `make-instance`, including at
  `--top 1`. If the matching command is absent, the outcome names the missing
  capability instead of recommending the opposite mutation. Compound requests
  name the missing `method-staticness-workflow` and give manual steps; standalone
  static-import and static-class inventory requests retain catalogue discovery. A
  later negation or class task does not hide an earlier positive conversion;
  earlier class/import tasks do not hide a later conversion. Polite
  punctuation and recognized motives remain a single conversion request, while
  an unrecognized surrounding clause or caller-update constraint returns the
  missing workflow instead of a bare mutation. This is
  command selection only; supplied names and signatures preserve direction but
  do not select a concrete target. Unparsed explicit method syntax returns the
  missing workflow rather than opposite-direction ranking. Method resolution,
  preparation, and verification
  remain open. Method-to-property/field requests return named
  `Unsupported(method-kind-conversion)` rather than a staticness recipe, while
  property/field staticness requests name `member-staticness-conversion` rather
  than a method workflow; static-member creation and deletion retain general
  discovery, which can still rank irrelevant commands and needs a later route.
  Earlier or later class, import, or member-deletion clauses do not hide a
  property or field conversion.
  Other changes to an already-static property or field and non-migration class
  context mentioning properties or fields retain general discovery.
  Static method/analysis context does not count as a conversion target;
  add/remove static modifier forms on a property or field name the missing
  member capability.
  Named, qualified, quoted, plural, and mixed property/field requests retain
  that member kind. An earlier unrelated or negated member action does not
  hide a later positive conversion; standalone negated member requests retain
  discovery.
  A later class or import task does not hide an earlier explicit but unparsed
  method conversion; an earlier class or import task also does not hide a later
  qualified unparsed method conversion.
  Named `make` and `remove static` forms also preserve
  direction. The independently named missing plan capability keeps its
  existing outcome.
  Negated method conversion with no follow-on task now returns `NoMatch` rather
  than ranking a mutation. Generic caller or usage inspection returns
  `Unsupported(method-relationship-target)` until the query names a method;
  a separate exact named-reference clause receives the existing read workflow.
  Unrecognized follow-on tasks stay out of mutation ranking. General goal
  routing and caller-specific workflows remain open.
  Explicit static-class-to-instance and injection requests now return
  `Unsupported(static-class-migration)` with no method recipe. The reason points
  to `static-class-inventory` as preliminary read evidence while complete
  caller, interface, and registration migration remains unavailable.
  Independent review extended the route to direct instance-class, migration,
  replacement, and dependency-injection wording, and protected negation and
  separate-action boundaries.
  A further boundary pass required named-source and injectable-service forms
  while excluding contextual instance-class mentions and negated targets.
  Another pass covered member-qualified sources, demonstratives, and direct
  instance targets while excluding `with` context and modified prohibitions.
  The latest boundary pass covers quoted type names and comma-separated member
  qualifiers, while rejecting action words as bare type names and instance
  member destinations as class migrations.
  Explicit static property/field creation and ordinary deletion now return
  `Unsupported(static-member-creation)` and `Unsupported(static-member-deletion)`
  with empty results, rather than ranking import, method, or unproven delete
  mutations. Existing-member modifier conversion, imports, inventory, and
  standalone negation keep their distinct routes. Attribute changes on static
  members still need task-aware relevance handling.
  Explicit edits to references or usages of static properties and fields now
  return `Unsupported(member-reference-edit)` with empty results. The reason
  points to exact reference inspection and distinguishes reference edits from
  `delete-unreferenced`, which prepares deletion of the declaration.
  The same route now covers edits to read, write, and access sites. Declaration
  accessibility, attributes, accessors, and initializers remain separate
  relevance work; a named read search retains its inspection workflow.
- Prepared mutations now reject outside paths, parent traversal, and existing
  symlink components before staging, including extraction output (ADR-0084).
  Staged changed and added Roslyn documents now retain explicit source-text
  encoding and preamble; standalone prepared text writes keep their existing
  UTF-8-without-BOM default.
  The legacy `SolutionWriter` direct apply branch has been removed and its
  transformation fixtures now use the prepared writer. Path-swap races remain
  open; this does not satisfy the optional apply admission gate. Preview now
  validates prepared affected paths before output, rejecting pathless changed
  or added documents and conflicting affected paths without a partial diff.
  Removed documents now produce deletion previews; project additions/removals
  fail before output, matching prepared apply support. Prepared change-set
  construction now rejects identified non-source document, solution
  analyzer-reference/option, project reference/metadata, and source-document
  metadata/order deltas before preview or apply, including mixed changes that
  previously produced partial results. Supported representations for those
  deltas remain future work; audit further Roslyn snapshot state before an
  exhaustive admission claim.
  The current TypeToProject operation now has disk-level coverage proving
  dry-run byte preservation, prepared apply across source/project/reference
  paths, and sibling-prefix source rejection. The obsolete test-only
  `ExecuteCrossProjectMoveAsync` path and its direct project-file save helper
  have been removed; meaningful behavior checks now target `PrepareAsync` and
  `MoveOperation`. The test-only `MoveSourceFiles`, single-type, and namespace
  direct-write entrypoints are now retired after their useful behavior checks
  moved to prepared preview/apply tests. `MoveService` no longer calls `File.Move`
  or holds a solution writer; broader apply admission still depends on the
  remaining platform gates.

## Dependencies and reconciliation

- `critical-platform-hardening` remains authoritative for trust, workspace
  completeness, mutation safety, MCP conformance, and release gates.
- RT-007 remains authoritative for shared descriptors and typed application ports.
- RT-012 remains authoritative for portable distribution. Its
  [execution topology authority](rt-012-portable-mcp-installation-and-distribution.md#execution-topology-authority)
  now reconciles the historical daemon-owner direction with ADR-0008 and the
  daemon-free preview target.
- ADR-0002's adapter-owned suggestion model must be superseded by host-neutral
  catalogue metadata and shared workflow recommendations.
- The dirty `codex/p0-mutation-containment` branch must be finished and verified as
  its own slice; this issue does not authorize overwriting or bypassing that work.

## Explicitly deferred

- mutation apply through MCP or daemon;
- daemon workspace caching;
- cross-host mutation coordination and restart-safe recovery;
- isolated evaluation of untrusted repositories;
- a general third-party rule/recipe plugin runtime;
- IDE integration and installer proliferation.

Those capabilities may be required for a later stable release. They are not required
to obtain useful feedback from an honest read-only and plan-only `0.1-preview`.

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-10-03 | Link the product-spine authority from architecture navigation | The overview and ADR index link to this issue and the existing OpenSpec, keeping scope and tasks at their original owners. RT-012 topology reconciliation is recorded there; preview admission remains open. |
| 2026-10-03 | Record skipped-test preview dispositions | The owning OpenSpec records nine retained skipped test identities: four require fixes before relevant admission and five are explicitly experimental. No skip removal, defect fix, or preview readiness is claimed. |
| 2026-10-03 | Reconcile typed-port migration and canonical-contract status | The [generated status view](../../../command-contract-status.md) links archived RT-007 acceptance, declared manifest work states, and the unchecked platform-hardening P1.1 tasks. It does not execute runtime gates or close those broader admission tasks. |
