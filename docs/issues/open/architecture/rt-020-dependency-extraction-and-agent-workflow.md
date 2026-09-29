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
- Baseline: main is clean at `517a32e`; fetched origin/main matches. Focused characterization passed: 176 Core/CLI and 20 MCP tests.

- Preparation: add the outer adapter project and inward graph; explicitly declare test project dependencies used by their source.

- YAML: removed Core YamlDotNet reference; decision data stays in Core and real YAML persistence is injected at the CLI. Focused checks passed 16 Core/CLI and 8 MCP tests. Manual extraction checkpoint: `0e6ba34`; rt suggestions do not provide a persistence-boundary extraction.

- Renderer interface: rt extraction at clean `b52e9db` changed exactly the two previewed files; post-preview HEAD/status were unchanged. All 154 semantic-diff tests passed.

- Composition: Hosting no longer references Scrutor; adapter registration scans the Core and Adapter assemblies, and semantic-diff consumes its neutral port. 176 Core/CLI and 31 MCP tests passed from clean `447ce36`; scoped format verification passed.

- Renderer move: rt move-slice safely refused incomplete acquisition evidence and left clean `86c3006` unchanged. Used a byte-identical file move after rt caller inspection; 154 Core/CLI and 20 MCP tests passed.

- Solution previews: DiffPlex and the console preview adapter now live outside Core/Hosting. The neutral sink carries document changes; existing output and prepared-write tests passed (181 Core/CLI, 30 MCP). Clean checkpoint `1e63fe7`.

- Dependency guard: evaluated direct/imported package and outward project references fail with RTDEP001/RTDEP002. Six injected violations were RED before enforcement; all seven policy tests and 19 host boundary tests now pass. Compiled Core/Hosting references and the rt architecture graph enforce the inward boundary.

## Findings to aggregate

- Relationship responses carry very large workspace diagnostic envelopes; inspect compact structured payloads while preserving completeness limitations.
- Workspace acquisition reports partial evidence for transitive test-project references; do not treat relationship absence as complete proof.
- MCP suggest advertises no workspace argument but inherits requirements that reject a workspace-free call.
- Agent skill and feedback documentation contain stale command paths and capability claims.

## Related work

Shared reflection changes are owned by the ai-rules repository in an isolated worktree. Its existing unrelated working changes and unpublished commit must be preserved.
