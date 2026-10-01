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
- The staged write for a changed or added Roslyn document retains an explicit
  `SourceText.Encoding`, including its preamble. Standalone prepared text writes
  retain the existing UTF-8-without-BOM default. This closes one byte-preservation
  gap but does not settle newline or file-mode policy.
- This is a path-string preflight, not a race-free effect broker. Another process
  can replace a path component after the check. `SolutionWriter` now exposes
  preview reporting only; `SolutionChangeWriter` apply and migrated
  transformation test fixtures use the prepared writer. This does not establish
  unattended apply safety. Preview now validates a `PreparedChangeSet` before
  rendering any diff, so a pathless changed or added document or conflicting
  affected path fails without a misleading partial report. It renders removed
  documents as deletions and rejects project additions or removals before output.
  `PreparedChangeSet` also rejects identified unsupported solution deltas at
  construction: non-source documents, solution analyzer references and options,
  project references and metadata, and source-document metadata or order
  changes. This prevents a partial preview or apply when a supported source
  edit accompanies one of these unsupported deltas.
- On systems where the trusted checkout is reached through a symlinked ancestor,
  the preflight rejects the alias path. Callers must use physical paths until a
  handle-based boundary can safely support aliases.
- A later slice must use handle-relative, no-follow operations or refuse apply
  where the physical boundary cannot be proven. The preview mutation admission
  gate remains open.

## Alternatives considered

- **Check paths immediately before commit only.** Rejected because staging already
  writes beside the destination.
- **Infer a root from affected paths.** Rejected because an untrusted output path
  could then define its own authority.

## References

- [Architecture overview](../architecture-overview.md)
- [Surface audit](../../openspec/changes/shape-pre-release-product-spine/pre-freeze-surface-audit.md)
