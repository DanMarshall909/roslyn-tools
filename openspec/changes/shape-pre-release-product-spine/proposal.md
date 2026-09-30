# Shape the pre-release product spine

- Status: proposed
- Priority: P0/P1
- Target: `0.1-preview`
- Tracking: `rt-016`
- Depends on: `critical-platform-hardening`, RT-007, RT-012

## Why

`rt` has accumulated useful Roslyn operations, but the current breadth presents too
many experiments as equivalent product commitments. The generated catalogue contains
56 ordinary commands, 33 of which mutate source, while the installed MCP surface
contains 61 tools after ancillary operations are included. The surface is broadest in
bespoke transformations and weakest in the connective workflow that developers and
agents need:

```text
orient -> discover -> understand -> assess impact -> plan -> verify
```

The repository already has an extensive platform-hardening change covering trust,
workspace completeness, mutation recovery, canonical operations, MCP conformance,
resource bounds, and release engineering. This change consumes those guarantees and
answers a different question: which coherent product can ship before every ambitious
platform capability is complete?

## Product promise

`0.1-preview` SHALL be described as:

> A trustworthy, structured C# understanding and change-planning tool for explicitly
> trusted local repositories.

Stable preview capabilities are read-only. Selected refactors may prepare immutable,
revision-bound plans, but MCP and daemon paths do not apply them. An explicitly
experimental direct CLI apply path may exist only if its narrower safety contract is
implemented and tested; the package remains useful without it.

## What Changes

This change establishes:

- a pre-freeze, command-by-command workflow audit that judges usefulness and
  safety before token cost, then one reviewed disposition and a
  `Stable | Preview | Experimental | Maintainer-only | Removed` maturity model;
- a smaller public CLI/MCP surface with no stable daemon dependency;
- one versioned capability and result contract carrying safety, prerequisites, cost,
  source/workspace provenance, diagnostics, limitations, and next actions;
- `doctor`, shared solution/symbol discovery, semantic relationships, compiler and
  analyzer diagnostics, change impact, common checks, baseline comparison, and plan
  verification;
- workflow-aware suggestions generated from host-neutral catalogue metadata;
- a common finding model for analyzers and a plan-first recipe model for opinionated
  codemods;
- revision-bound, write-free planning for a small refactor whitelist;
- a phased delivery plan whose slices are independently testable and can land without
  disturbing the current mutation-containment work;
- preview release acceptance journeys and explicit deferrals.

This is intentionally **BREAKING** for the unreleased command surface: incomplete,
misleading, maintainer-only, and opinionated commands may be renamed, consolidated,
demoted, hidden, or removed before compatibility is promised.

## Capabilities

### New Capabilities

- `product-spine`: Defines preview maturity and surface admission, workspace/capability
  truth, discovery, relationships, diagnostics, impact, checks and baselines,
  workflow guidance, revision-bound planning, verification, and release journeys.

### Modified Capabilities

- None. The existing `critical-platform-hardening` delta remains authoritative for
  platform trust, completeness, mutation, MCP, resource, and release guarantees.

## Non-goals

- Re-specifying the trust, completeness, durable mutation, MCP protocol, or release
  guarantees already owned by `critical-platform-hardening`.
- Shipping mutation apply through MCP or daemon in `0.1-preview`.
- Building daemon workspace caching before measured performance justifies it.
- Treating a local checkout as trusted without explicit user approval.
- Providing proof of whole-world non-use for externally visible or dynamically
  activated symbols.
- Converting every existing implementation before the public façade is proven.
- Adding another standalone transformation while the product spine is incomplete.
- Introducing a generic untyped `execute_command` escape hatch.

## Decisions

### Release topology

- The supported preview topology is direct CLI plus a standards-conformant MCP host.
- The daemon is hidden from preview discovery and packaging. Existing daemon work may
  remain an internal experiment, but preview correctness does not depend on it.
- Semantic workspace evaluation is restricted to explicitly approved local
  repositories. A typed trust-required outcome is returned otherwise.
- `NoEvaluation` operations such as declaration-only semantic diff may remain
  available where their limitations are explicit.

### Public surface

The target public capability families are:

1. workspace truth: `doctor` and capabilities;
2. symbols: search, outline, describe, source, and relationships;
3. analysis: diagnostics, semantic diff, dependency graph, impact, check, inventory;
4. change: prepare, verify, and—outside supported MCP preview—explicit lifecycle;
5. guidance: workflow recommendation.

Typed operations with materially different schemas remain distinct. Internal engines
may consolidate even where thin CLI conveniences remain.

### Maturity

- **Stable candidate:** read-only, versioned structured contract, complete provenance,
  bounded output, CLI/MCP parity, packaged integration coverage.
- **Preview:** useful contract with clearly labelled limitations; refactors are
  plan-only through MCP.
- **Experimental:** opt-in CLI recipe or advanced refactor with no stable compatibility
  promise.
- **Maintainer-only:** dogfood, feedback administration, trace diagnostics, generated
  documentation, and internal performance controls.
- **Removed:** misleading, incomplete, unrelated, or compatibility-only surface that
  has no released users.

### Rules and recipes

- Analysis findings share stable rule IDs and one envelope. `solid` branding is
  replaced by concrete evidence-based rules.
- Opinionated or framework-specific codemods become experimental recipes backed by
  the same revision-bound plan and verification model.
- Standard Roslyn compiler/analyzer diagnostics and code actions are preferred over
  inventing additional micro-refactors where their safety contract is adequate.

## Delivery strategy

1. Audit the live CLI/MCP surface against representative user and agent jobs,
   measure output/call cost, then classify and freeze the supported surface.
2. Finish mutation containment and remove apply from supported MCP discovery.
3. Establish shared descriptors, structured results, provenance, and `doctor`.
4. Deliver symbol discovery, relationships, and workflow recommendations.
5. Deliver diagnostics, common checks, baselines, impact, and conservative test/API
   analysis.
6. Deliver revision-bound plans and verification for a small mutation whitelist.
7. Move remaining codemods into recipes or maintainer-only/removed states.
8. Pass packaged MCP and cross-platform preview release gates.

Each stage proves one representative vertical slice before parallel conversion. Shared
registries, schemas, generated documentation, and architecture allowlists remain
coordinator-owned.

## Success measures

- The supported MCP surface contains no apply-capable operation.
- Every advertised preview tool has accurate maturity, safety, prerequisites, cost,
  completeness needs, and bounded structured output.
- Ambiguous symbols and solutions produce deterministic candidates rather than a
  silent choice.
- An interface or public-contract change can produce implementations, callers,
  projects, diagnostics, API risks, and candidate tests in one impact workflow.
- Baseline comparison distinguishes introduced, unchanged, and removed findings.
- A reviewed plan becomes stale when its source preconditions drift and is rejected
  before any write.
- Partial workspaces never emit proof-like deletion advice or a deletion plan.
- Packaged CLI/MCP acceptance tests pass on supported operating systems without build
  or installation side effects.
- New standalone transformation work remains frozen until these outcomes are boring
  and repeatable.

## Impact

- Public CLI and MCP command names, schemas, discovery, maturity, and output contracts.
- Host-neutral application descriptors and the shared symbol/workspace/result models.
- Existing analyzers, inventories, refactors, feedback tools, daemon controls, and
  generated command documentation.
- Package profiles, protocol tests, cross-platform release gates, and user-facing
  compatibility expectations.
- ADR-0002, RT-007, RT-012, and the relationship to
  `critical-platform-hardening`; no current C# implementation is changed by this
  proposal itself.
