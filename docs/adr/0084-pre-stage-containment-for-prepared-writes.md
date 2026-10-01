# ADR-0084: Check prepared mutation paths before staging

- Status: Accepted
- Date: 2026-10-01
- Related OpenSpec: [pre-release product spine](../../openspec/changes/shape-pre-release-product-spine/proposal.md), [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

`extract-class` and `extract-interface` accept an output directory. Their prepared
changes reach the shared writer, which previously created destination directories
and adjacent stage files before checking whether those paths stayed within the
trusted checkout. A check only at commit would be too late. Roslyn document paths,
file edits, deletions, and moves use the same staging boundary.

## Decision

Before any staging or recovery directory is created, the prepared writer requires
an authority-derived repository root. It uses the invocation's explicit root when
present, otherwise the trusted identity of the original solution. An in-memory
solution without either root fails closed. The writer checks the raw paths and the
built plan for parent traversal, lexical containment, and existing symlink or
reparse-point components. This covers solution documents, new files, text and
project edits, deletions, both move endpoints, and reported affected paths. An
internal explicit-root constructor and test-only trusted workspace override let
in-memory fixtures establish the same boundary without inferring authority from
the path being mutated.

## Consequences

- Known outside paths and symlinked output directories are rejected before any
  stage file or destination directory is created. The two extraction operations
  use this common writer and have direct outside-output tests.
- This is a path-string preflight, not a race-free effect broker. Another process
  can replace a path component after the check. The internal legacy
  `SolutionWriter.ApplyChangesAsync(false)` branch remains a direct writer used by
  older tests; the production `SolutionChangeWriter` invokes it only for dry-run
  reporting. An architecture test guards that production call shape. Neither
  branch establishes unattended apply safety.
- On systems where the trusted checkout is reached through a symlinked ancestor,
  the preflight rejects the alias path. Callers must use physical paths until a
  handle-based boundary can safely support aliases.
- A later slice must close the direct writer bypass and use handle-relative,
  no-follow operations or refuse apply where the physical boundary cannot be
  proven. The preview mutation admission gate remains open.

## Alternatives considered

- **Check paths immediately before commit only.** Rejected because staging already
  writes beside the destination.
- **Infer a root from affected paths.** Rejected because an untrusted output path
  could then define its own authority.

## References

- [Architecture overview](../architecture-overview.md)
- [Surface audit](../../openspec/changes/shape-pre-release-product-spine/pre-freeze-surface-audit.md)
