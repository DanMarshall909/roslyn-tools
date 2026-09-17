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

## 7. Astra review remediation

- [x] 7.1 Reopen the accepted change with explicit compatibility, freshness, liveness, and canonical-root scenarios from the Astra review.
- [x] 7.2 RED/GREEN ordinary CLI solution resolution, installation-only doctor routing, and request-relative outline targets without restoring process-wide current-directory mutation.
- [x] 7.3 RED/GREEN conservative implicit NuGet/evaluated build inputs and filesystem-dependent MSBuild conditions.
- [x] 7.4 RED/GREEN non-blocking cancellable cache maintenance and canonical explicit symlink trust roots.
- [x] 7.5 Run focused coverage and changed-scope mutation analysis, the strict OpenSpec validations, formatting, and the full local gate.
- [x] 7.6 RED/GREEN daemon-boundary CLI solution/outline normalization and bounded eviction past busy identities.
- [x] 7.7 RED/GREEN effective restore paths, recursive package imports, filesystem-valued build expressions, and file/directory existence transitions.
- [x] 7.8 Rerun focused coverage, strict OpenSpec validation, formatting, the full local gate, and real MCP/daemon acceptance.
- [x] 7.9 RED/GREEN conservative property-function evaluation, effective `ProjectAssetsFile`, and identical normalized CLI requests across daemon, direct, and pre-dispatch fallback execution.
- [x] 7.10 RED/GREEN conditional restore ancestry, SDK-glob source additions, unsupported task inputs, diagnostic doctor discovery, and invocation-scoped trust locality from the final Astra review.
- [x] 7.11 RED/GREEN analyzer-config discovery, implicit project-extension imports, nested output-name sources, target-scoped restore assignments, and local diagnostic doctor admission from the second final Astra review.
- [x] 7.12 RED/GREEN exact default project-output exclusions and fail-closed imported output customization so live build artifacts cannot destabilize cache admission.
- [x] 7.13 RED/GREEN overlapping project output discovery, SDK artifacts-output redirects, and implicit SDK import-path overrides from the third final Astra review.
- [x] 7.14 RED/GREEN MSBuild expressions/escaping, case-insensitive semantic names, restore-property precedence, and imported item path context from the fourth final Astra review.
- [x] 7.15 RED/GREEN Razor view discovery, default MSBuild wildcard hooks, restore-path escaping, and generated NuGet path context from the fifth final Astra review.
- [x] 7.16 RED/GREEN system-hook dependencies, build-defined hook roots/selectors, cross-targeting hooks, and Visual Studio-version wildcards from the sixth final Astra review.
- [x] 7.17 RED/GREEN baseline and transitive system-hook validation, NuGet package-root precedence, default custom Common/language hooks, unknown and import-qualified SDK declarations across every parser, test-platform and initial-target scheduling, MSBuild-case environment properties, worktree-specific Git revision inputs, effective native and Source Link Git configuration, minimal Git metadata, invalid-marker ancestor discovery, Source Link task-assembly overrides, project-tree and explicit external assembly-candidate precedence, custom reference extensions, direct or configured ruleset graphs, and semantic strong-name signing inputs from the seventh through eighteenth final Astra reviews.
- [x] 7.18 Freeze the repaired candidate, obtain final Astra PASS, then commit and push the accepted slice without publishing or deleting worktrees; merge only to the user-confirmed target.
