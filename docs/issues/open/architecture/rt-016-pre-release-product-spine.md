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
  Older cross-project move helpers exercised by tests still contain direct `File.Move` and
  project-file save effects; audit them in another containment slice before
  any apply admission claim.

## Dependencies and reconciliation

- `critical-platform-hardening` remains authoritative for trust, workspace
  completeness, mutation safety, MCP conformance, and release gates.
- RT-007 remains authoritative for shared descriptors and typed application ports.
- RT-012 remains authoritative for portable distribution, but its older decision
  making the daemon the execution owner must be reconciled with ADR-0008 and this
  preview topology.
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
