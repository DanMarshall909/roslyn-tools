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

The current generated catalogue exposes 56 ordinary commands: 23 read-only and
33 mutating. Forty-one catalogue entries do not advertise JSON output, while the
installed MCP surface exposes 61 tools after suggestion and feedback operations
are included. The breadth is concentrated in bespoke refactors even though the
beginning and end of the agent workflow remain incomplete:

```text
orient -> discover -> understand -> assess impact -> plan -> verify
```

Several current surface contracts are also misleading or incomplete:

- `rt suggest` truncates changed files before reporting the context count and then
  recommends only `outline` calls;
- `convert-static-class` advertises interface generation and DI registration while
  explicitly skipping both;
- the command-oriented `add-test` acceptance contract retains skipped tests;
- `feedback_add` and `feedback_process` write state while their descriptors classify
  them as read-only;
- `probe-di` loads a repository assembly and invokes its registration method;
- MCP schemas and structured results are not yet consistent enough to treat every
  projected command as a supported product contract.

The existing `critical-platform-hardening` OpenSpec already owns repository trust,
workspace completeness, mutation containment and recovery, canonical operation
parity, MCP conformance, resource bounds, and release engineering. Adding product
surface work to its 134-task programme would obscure both scopes.

## Decision

Create one dependent OpenSpec change:

- [shape-pre-release-product-spine](../../../../openspec/changes/shape-pre-release-product-spine/proposal.md)

It defines the exact `0.1-preview` maturity tiers, command disposition, shared
finding and recipe models, workflow-aware discovery, diagnostics, change impact,
revision-bound planning, and verification. Platform guarantees remain owned by
`critical-platform-hardening` and are referenced as admission gates.

## Acceptance criteria

- [ ] Before final surface freeze, review the live CLI/MCP catalogue against
      representative user and agent jobs. Record every command's usefulness,
      overlap, safety, audience, and disposition, then measure calls/output cost
      without sacrificing completeness evidence. The interim command-admission
      guard is not final ratification; see the
      [pre-freeze audit](../../../../openspec/changes/shape-pre-release-product-spine/pre-freeze-surface-audit.md).
- [ ] Every current command has one recorded `Stable candidate | Preview |
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
