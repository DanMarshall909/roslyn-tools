# ADR-0085: Retire legacy direct-write move paths

- Status: Accepted
- Date: 2026-10-01
- Related issue: [RT-016](../issues/open/architecture/rt-016-pre-release-product-spine.md)
- Supersedes: [ADR-0062](0062-legacy-move-preview-result.md)

## Context

The public CLI, MCP, and daemon move command already uses `MoveOperation` to
prepare a change set before preview or apply. Older internal test entrypoints in
`MoveService` still applied source and project changes through a separate writer
and `File.Move`. ADR-0062 kept one of those helpers from rendering directly to
the process console, but it did not make that path share the prepared mutation
contract.

## Decision

Retire the test-only direct-write move entrypoints and migrate their useful
behavior checks to `MoveService.PrepareAsync` and physical `MoveOperation`
preview/apply tests. `MoveService` now computes moves without holding a logger
or solution writer. The host operation executes the prepared change set.

## Consequences

- Type, namespace, and cross-project moves have one supported host path for
  preview and apply.
- Physical move tests verify that dry-run preserves bytes, apply relocates and
  rewrites files, and invalid destinations or source folders fail before writes.
- ADR-0062 remains the historical transitional decision; its helper no longer
  exists. Broader apply admission still depends on the remaining platform gates.

## Alternatives considered

- Keep the legacy methods behind internal visibility: rejected because tests
  could continue to validate a second write path instead of the public contract.

## References

- Commits: `9006e3f`, `6195183`
- Related ADRs: [ADR-0062](0062-legacy-move-preview-result.md),
  [ADR-0084](0084-pre-stage-containment-for-prepared-writes.md)
