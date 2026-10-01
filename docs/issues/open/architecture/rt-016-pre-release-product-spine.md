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

The 2026-10-01 source-built catalogue exposes 60 entries: 25
inspect/analyze/CI, 34 refactor (including the local `trust` control), and one
lifecycle command. Thirty-nine entries do not advertise JSON output even though
several can emit JSON. The MCP surface under the current persistent-trust
profile exposes 60 tools: 58 shared with CLI plus `prime` and `suggest_command`.
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
- root help omits usable discovery entry points, while MCP output schemas are
  generic and some request properties are undocumented;
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
input effects need separate attribution and proof before a complete claim.

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

- The durable `prepare-operation` primitive now has an explicit maintainer CLI
  route. Public help, task suggestions, command reference, site catalogue, and MCP
  discovery exclude it until a complete plan lifecycle is available (ADR-0082).

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
