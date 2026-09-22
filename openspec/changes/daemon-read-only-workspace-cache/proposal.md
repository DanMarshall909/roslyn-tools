# Read-only workspace reuse

- Status: accepted
- Decision owner: architecture
- Supersedes narrowly: daemon workspace-cache prohibition for the six named read-only operations

## Why

Read-only MCP semantic calls currently reopen the same trusted solution for every
request, making the normal doctor-to-navigation workflow pay repeated MSBuild and
Roslyn load costs. The persistent daemon and MCP host already provide a bounded
process lifetime in which an immutable, revision-bound workspace can be reused
without changing tool arguments or granting the daemon mutation authority.

## What Changes

- Add a bounded, daemon-lifetime cache of leased read-only Roslyn workspace
  snapshots for trusted solutions.
- Coalesce concurrent cold acquisition for one cache identity and allow queries to
  execute concurrently after acquiring the immutable snapshot.
- Key entries by canonical solution path, canonical worktree root, repository trust
  identity, and semantic environment; never share an entry across linked worktrees.
- Prove freshness from source and semantic build inputs before reuse, and reload on
  any relevant change or whenever freshness cannot be established.
- Resolve relative solution paths only beneath an explicit trusted repository root;
  reject ambiguous, escaping, or process-current-directory-relative requests.
- Route only doctor, outline, describe, search_symbols, source, and relationships
  through this read-only path. Mutation dispatch, fallback, completeness,
  confidence, trust, and cancellation contracts remain unchanged.
- Emit bounded cache observations for deterministic operational evidence without
  changing public MCP tool arguments or result payloads.

## Capabilities

### New Capabilities

- `read-only-workspace-reuse`: Safe, revision-consistent reuse, refresh, isolation,
  leasing, eviction, and observability for trusted read-only semantic operations.

### Modified Capabilities

None. This accepted change is a narrow exception to the cache prohibition in the
still-proposed `critical-platform-hardening` change; that proposal and its daemon
ADR are updated in the same slice so the two authorities do not conflict.

## Impact

The change affects the workspace acquisition seam, the six named read-only
application operations, daemon/MCP read-only placement and path admission, daemon
cache telemetry, and their focused tests. It adds no package dependency and does
not add daemon-owned mutation, durable sessions, cross-machine state, or a generic
workflow/session API.
