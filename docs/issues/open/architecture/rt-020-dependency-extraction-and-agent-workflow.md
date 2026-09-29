# RT-020: Dependency extraction and agent workflow

- Status: In progress
- Authority: accepted implementation plan in the 2026-09-29 development session

## Acceptance

1. Core and Hosting have only approved Microsoft/.NET direct application dependencies. Roslynator remains build tooling; Microsoft's transitive packages remain permitted. Third-party adapters preserve YAML, diff, registration, and host behavior.
2. Agents discover valid CLI/MCP operations through compact shared metadata, workspace-free discovery, executable examples, and current entrypoint guidance.
3. Shared ai-rules requires quiet continuous reflection and aggregate reporting at handoff. Project guidance specializes it; new standing-rule proposals still require review.

## Execution contract

Every refactoring starts at a verified clean commit. Record HEAD and full Git status before its dry-run; require the same HEAD and empty status afterward before applying. Compare preview and actual diff, test, and commit before the next refactoring. Preserve unexpected changes as defect evidence. Manual extraction uses the same clean checkpoints.

Reflection accumulates between operations without interrupting progress, except for active correctness/safety failures or a blocker requiring user authority. Use existing feedback records for tool defects. Raw test and checkpoint evidence is retained under ignored `artifacts/dependency-extraction/`.

## Progress

- Planning: package/usage audit through rt identified DiffPlex and YamlDotNet in Core, Scrutor in Hosting, and Microsoft-transitive Humanizer. Scope and exceptions confirmed.
- Baseline: main is clean at `517a32e`; fetched origin/main matches. Focused characterization tests are running.

## Findings to aggregate

- Relationship responses carry very large workspace diagnostic envelopes; inspect compact structured payloads while preserving completeness limitations.
- Workspace acquisition reports partial evidence for transitive test-project references; do not treat relationship absence as complete proof.
- MCP suggest advertises no workspace argument but inherits requirements that reject a workspace-free call.
- Agent skill and feedback documentation contain stale command paths and capability claims.

## Related work

Shared reflection changes are owned by the ai-rules repository in an isolated worktree. Its existing unrelated working changes and unpublished commit must be preserved.
