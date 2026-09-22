---
tags:
  - refactoring
  - architecture
  - mutation-safety
  - mcp
ticket: rt-018
severity: High
status: Resolved
area: commands
owner: ""
reported: 2026-09-08
resolved: 2026-09-08
summary: "Add a strict typed operation that moves a reviewed architectural slice across projects and namespaces as one transaction"
---

# RT-018: Add transactional architectural slice move

## Context

The single-target `move` operation is useful for narrow symbol changes, but an
architecture migration needs a coherent multi-file boundary. Wage Claims exposed
specific gaps around renamed project roots, recursive namespace reporting, parent
namespace bindings, relative namespace qualifiers, multi-declaration files, and
cross-project edits. Repeating smaller mutations makes the intended slice hard to
review and increases the opportunity for partial state.

## Decision

Add a distinct typed `move-slice` CLI operation, projected as MCP `move_slice`. Keep
`move` focused on its existing modes. The new operation owns one strict contract:

- exactly one explicit type list or recursive source namespace;
- required target project and target namespace;
- dry-run by default with a complete typed manifest;
- optional dependency-closure evidence that never expands the selection;
- project-reference and `InternalsVisibleTo` edits only after an explicit request and
  non-empty justification;
- complete workspace, unambiguous symbols, contained physical paths, collision-free
  destinations, and error-free projected compilations before application;
- one prepared change set for all source, reference, file, and project edits.

## Acceptance criteria

- [x] CLI and MCP bind the same typed request and return the same structured result.
- [x] Multiple explicit types and recursive namespaces report every moved type,
      changed document, file move, and project edit deterministically.
- [x] Files with multiple declarations are indivisible and every moved declaration is
      disclosed in the manifest.
- [x] Dependency analysis reports required related types with source evidence and never
      silently includes them.
- [x] Implicit project or friend-assembly changes are rejected; explicit requests require
      a justification and appear in the manifest.
- [x] Workspace, binding, compilation, path, and destination failures do not write.
- [x] Apply uses the prepared transactional writer and injected failures roll back.
- [x] Wage Claims regressions cover project-root rename order, recursive result fidelity,
      former-parent imports, relative qualifiers, and cross-project slices.
- [x] Trusted stdio MCP discovery/invocation succeeds; untrusted discovery omits the tool.

## Non-goals

- Automatically choosing an architecture or broadening the selected slice.
- Automatically adding dependencies merely to make compilation succeed.
- Removing now-unused project references or changing package references.
- Replacing the durable mutation-kernel work tracked by the platform-hardening programme.

## Progress

### 2026-09-08: typed host adapters

- Added the thin `move-slice` CLI adapter over `MoveSliceOperation`.
- The generic MCP descriptor adapter exposes the same typed port as `move_slice` only
  for startup-trusted repositories.
- CLI and MCP integration tests prove that omitted `dry-run` remains review-only and
  leaves source and destination paths untouched.
- Focused verification: 15 CLI/core move-slice tests and 2 MCP move-slice tests pass.

### 2026-09-08: fail-closed review hardening

- Project-reference cycle detection now evaluates the complete pending reference graph,
  so reciprocal additions produce a typed diagnostic instead of a Roslyn exception.
- Every project-file edit is constrained to the resolved and explicitly trusted repository
  root.
- Qualified type references are rewritten with type syntax, and generated former-parent
  imports retain valid `using` token trivia.
- An injected mid-commit file-move failure restores the original rewritten source and
  leaves no destination behind.
- Added explicit friend-assembly manifest, destination-collision, Wage Claims namespace,
  and newline-framed trusted stdio protocol regressions.
- Focused verification: 22 CLI/core move-slice tests and 3 MCP/protocol tests pass.

## Resolution

Implemented `rt refactor move-slice` and MCP `move_slice` as thin host adapters over
one typed, in-process, review-default operation. The planner selects complete files,
rewrites declarations and bound references, reports dependency evidence, validates the
projected solution, and prepares source, file-move, project-reference, and friend-assembly
changes as one transactional change set.

Review hardening closes combined project-reference cycles, out-of-root project XML
writes, qualified type rewrites, former-parent imports, destination conflicts, and
mid-commit rollback. Wage Claims-style multi-file and namespace regressions, generic MCP
trust policy, and newline-framed stdio discovery/invocation are covered.

Verification on 2026-09-08: `scripts/check-local.ps1 -Full` passed with a zero-warning
build, 1,863 core/CLI tests, 110 MCP tests, 9 intentional skips, and whole-solution
formatting verification.
