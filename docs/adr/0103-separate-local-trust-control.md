# ADR-0103: Separate local trust control from repository refactoring

- Status: Accepted
- Date: 2026-10-02
- Related issue: [RT-016](../../openspec/changes/shape-pre-release-product-spine/tasks.md)
- Supersedes: The trust category decision in [ADR-0012](0012-review-first-mutation-defaults.md)

## Context

`rt trust` manages persistent user-scoped trust state. Its `add` and `remove`
verbs are explicit local decisions, while `list` and `status` inspect state.
The command inherited `IRefactorCommand`, so public help placed it under
`rt refactor` and claimed repository writes, explicit repository trust, a
semantic workspace, and a loadable solution. None of those requirements
described its actual CLI route.

## Decision

Make `IControlPlaneCommand` a direct `IRtCommand` category marker. Place trust
only at the root under the `control` discovery category. Keep `Mutates=false`
because that legacy flag means repository-source mutation. Publish a shared
CLI-only product profile with the conservative union of possible Git reads,
local-state reads, and local-state writes. The profile requires no existing
repository trust, semantic workspace, or .NET SDK. Keep the typed child ports
out of public application-port registration so MCP and daemon cannot expose
local trust management through the catalogue. Their internal definitions use
the same control category and local-state-write safety for add/remove, with
per-verb effects and Git requirements. Repeating add can refresh its stored
timestamp and Git anchor, so the aggregate and add profiles are conditionally
idempotent. Removal by stored id may report an absent record on a repeated call,
so its internal profile is conditional too.

## Consequences

- Root help labels `rt trust` as a local-state write. Structured help makes
  its root placement and host requirements explicit.
- `rt refactor trust` is removed. The pre-release root spelling remains.
- Add/status need a Git worktree; list and removal by stored id can run
  without one. Resource values remain advisory until CLI enforcement is added.
- Registry and Git work are synchronous; cancellation is only checked before
  the operation and is not advertised as cooperative.
- Repository-source review-first policy from ADR-0012 remains in force for
  refactor commands.
