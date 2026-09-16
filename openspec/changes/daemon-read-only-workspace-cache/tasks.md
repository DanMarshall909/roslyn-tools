## 1. Authority and architecture

- [x] 1.1 Add ADR-0017 and update ADR-0008, the architecture overview, the parked stage-2 task, and critical-platform-hardening wording so the read-only exception and remaining exclusions agree.
- [x] 1.2 Strictly validate the complete OpenSpec change before production implementation.

## 2. RED: acquisition, freshness, and lifetime

- [x] 2.1 Add deterministic tests proving sequential operations acquire one loaded snapshot and concurrent cold acquisition opens once.
- [x] 2.2 Add deterministic tests proving relevant file change refresh, separate solutions/worktrees remain isolated, and unprovable freshness reloads.
- [x] 2.3 Add deterministic tests proving bounded eviction never disposes a workspace while a query lease is active.
- [x] 2.4 Add path tests proving explicit-root relative resolution is independent of process current directory and cannot escape or select another checkout.

## 3. GREEN: read-only workspace cache

- [x] 3.1 Implement canonical trusted cache identity and conservative semantic-input fingerprinting with cancellation.
- [x] 3.2 Implement coalesced per-key loading, immutable query leases, bounded LRU retention, safe retirement/disposal, and injected telemetry.
- [x] 3.3 Move doctor, outline, describe, search_symbols, source, and relationships to the read-only lease seam and expose one snapshot revision without changing their inputs.

## 4. RED/GREEN: daemon boundary

- [x] 4.1 Add tests proving all six read-only operations prefer daemon execution while every mutation remains in-process and rejected by daemon dispatch.
- [x] 4.2 Add tests proving pre-dispatch daemon loss may fall back once, indeterminate dispatch cannot fall back, and mutation dispatch behaviour is unchanged.
- [x] 4.3 Add bounded daemon cache observations and path admission that never derives a solution from server process current directory.

## 5. Coverage and refactor

- [x] 5.1 Run focused coverage, classify uncovered branches, and add or remove behaviour only where the specification justifies it.
- [x] 5.2 Calibrate and run changed-scope mutation analysis for the cache, trust/path identity, and recovery policy; resolve meaningful survivors.
- [x] 5.3 Refactor production and test code under green tests, preserving the separate mutation acquisition seam.

## 6. Acceptance and handoff

- [x] 6.1 Run one real MCP/daemon process against a representative five-project fixture and retain load/hit/revision evidence plus cold and warm timings.
- [x] 6.2 Modify fixture source and prove the next MCP call refreshes to a new revision without changing Git HEAD.
- [x] 6.3 Run focused tests, strict OpenSpec validation, formatting, and the platform-equivalent full local gate (`pwsh` was unavailable; `bash scripts/check-local.sh --full` passed).
- [x] 6.4 Freeze the candidate, obtain independent completion review, repair any findings, then commit the exact reviewed slice without pushing, merging, publishing, or deleting worktrees.
