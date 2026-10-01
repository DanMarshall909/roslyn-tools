# ADR-0091: Discover semantic-diff paths from the actual baseline

- Status: Accepted
- Date: 2026-10-01
- Related issue: [RT-016](../issues/open/architecture/rt-016-pre-release-product-spine.md)
- Extends: [ADR-0087](0087-prove-git-content-absence-before-semantic-diff.md), [ADR-0090](0090-route-working-tree-review-with-platform-admission.md)

## Context

`semantic-diff --from <ref>` read its before-side content from `<ref>`, but
listed working-tree paths relative to HEAD. A clean working tree could therefore
report no changes even when commits after `<ref>` changed C# declarations. Git's
relative-diff configuration could also return paths relative to a nested current
directory, while content reads expected repository-root paths. A symbolic ref
could move between path discovery and content reads.

## Decision

Resolve each input ref to one commit object ID before discovering paths or
reading content. For a working-tree comparison, list tracked net changes with
`git diff --no-renames --no-relative --name-only -z <baseline-oid> --`, then add
untracked, nonignored paths across the repository root from
`git ls-files --others --exclude-standard --full-name -z -- :/`. The root
pathspec is required when the client starts in a nested directory. For
ref-vs-ref mode, diff the two pinned IDs with
`--no-relative`. Preserve NUL-separated literal paths and `--no-renames` so
both sides of a file move can be evaluated. A net-reverted tracked path is
omitted because its current content matches the baseline.

The Git client returns a typed acquisition failure when either path query
fails. Working-tree content remains subject to the contained Linux x64 reader;
pinning a commit does not make mutable working-tree files an atomic snapshot.

## Consequences

- `--from` alone now compares that ref with committed, staged, unstaged, and
  untracked current files, including changes on a clean tree.
- Direct CLI/MCP semantics and help now agree. Task-aware suggestion can still
  decline an ambiguous explicit baseline while naming the direct command; a
  separate routing slice can accept safely parsed refs.
- Git worktree files can change after path discovery. Content-read failures
  remain explicit rather than being treated as absence.

## References

- [Architecture overview](../architecture-overview.md)
- [Product-spine OpenSpec](../../openspec/changes/shape-pre-release-product-spine/specs/product-spine/spec.md)
