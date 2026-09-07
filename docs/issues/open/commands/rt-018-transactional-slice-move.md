---
tags:
  - refactoring
  - architecture
  - mutation-safety
  - mcp
ticket: rt-018
severity: High
status: Open
area: commands
owner: ""
reported: 2026-09-08
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
- [ ] Multiple explicit types and recursive namespaces report every moved type,
      changed document, file move, and project edit deterministically.
- [ ] Files with multiple declarations are indivisible and every moved declaration is
      disclosed in the manifest.
- [ ] Dependency analysis reports required related types with source evidence and never
      silently includes them.
- [ ] Implicit project or friend-assembly changes are rejected; explicit requests require
      a justification and appear in the manifest.
- [ ] Workspace, binding, compilation, path, and destination failures do not write.
- [ ] Apply uses the prepared transactional writer and injected failures roll back.
- [ ] Wage Claims regressions cover project-root rename order, recursive result fidelity,
      former-parent imports, relative qualifiers, and cross-project slices.
- [ ] Trusted stdio MCP discovery/invocation succeeds; untrusted discovery omits the tool.

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
