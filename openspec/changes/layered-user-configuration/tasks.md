## 1. Authority and baseline

- [x] 1.1 Record the accepted configuration, precedence, trust, and MCP composition contracts.
- [x] 1.2 Add ADR and architecture documentation for the new host configuration boundary.
- [x] 1.3 Run the existing configuration-adjacent CLI, daemon, and MCP tests as a baseline (58 CLI/core and 27 MCP tests passed).

## 2. RED/GREEN: configuration core

- [x] 2.1 Add failing tests for global and worktree path resolution, strict schema validation, and actionable file errors.
- [x] 2.2 Add failing tests for scalar precedence, intersected allowlists, accumulated denylists, and absent/empty semantics.
- [x] 2.3 Implement the typed loader, resolver, merger, and effective settings model without runtime integration.
- [x] 2.4 Add a local `rt config` inspection/validation command that remains usable when configuration is invalid.

## 3. RED/GREEN: daemon settings

- [x] 3.1 Add failing tests for file-based enablement, environment precedence, explicit auto-start suppression, and idle timeout selection.
- [x] 3.2 Route CLI and MCP daemon decisions through the effective configuration while preserving manual lifecycle commands.

## 4. RED/GREEN: MCP tool policy

- [x] 4.1 Add failing tests proving allow/deny policy changes discovery and invocation identically.
- [x] 4.2 Validate configured exact tool names against the public descriptor registry and fail startup on unknown names.
- [x] 4.3 Apply effective policy at the shared MCP availability seam without changing CLI command availability or trust admission.

## 5. Acceptance and publication

- [x] 5.1 Run focused tests, strict OpenSpec validation, formatting, Release build, and both test projects.
- [x] 5.2 Smoke-test global-only and repository-narrowed configuration with the published CLI and MCP hosts.
- [ ] 5.3 Review the final diff, commit logical slices with detailed bodies, and push the feature branch while preserving unrelated worktrees.
