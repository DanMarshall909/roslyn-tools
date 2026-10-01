# ADR-0087: Prove Git content absence before semantic diff

- Status: Accepted
- Date: 2026-10-01
- Related issue: [RT-016](../issues/open/architecture/rt-016-pre-release-product-spine.md)
- Extends: [ADR-0086](0086-fail-closed-on-git-change-acquisition.md)

## Context

Semantic diff treats a `null` content-at-ref result as a file that exists on
only one side of the comparison. The Git adapter previously returned `null`
for every failed `git show`, including invalid refs and unreadable objects.
Git can also return success with empty output for an absent path containing
pathspec characters. Either case can produce a false semantic diff.

## Decision

The Hosting Git adapter first asks `git ls-tree` for an exact, literal,
repository-root-relative path at the requested ref. A successful lookup with
no matching entry proves absence and returns `null`. For an entry that exists,
`git show` must succeed; lookup and read failures throw a contextual
`GitContentReadException`. The shared semantic-diff application operation maps
that exception to `RtError.ExternalToolFailure` with ref and path context, so
CLI and MCP report the same failed acquisition.
Changed-path acquisition uses NUL-delimited names and requests root-relative
untracked paths. Working-tree reads resolve those names against the repository
root, including when the selected Git directory is nested.

## Consequences

- Added and removed files remain distinct before-only and after-only sources.
- Tabs, newlines, and other quoted filename characters retain their literal
  spelling through acquisition and lookup.
- Invalid refs and unreadable blobs cannot appear as absent files or successful
  empty content. The literal lookup also avoids wildcard matches from file names.
- Content reads run an extra Git process for the tree lookup. Ref identity is
  not yet pinned across separate Git commands; a concurrently moving symbolic
  ref remains a follow-up for snapshot consistency.
- Working-tree file reads retain their filesystem contract but now use the
  repository root for Git-reported paths.

## Alternatives considered

- Interpret only a nonzero `git show` as missing: rejected because other read
  failures share that exit channel and an unmatched pathspec can exit zero.
- Treat all failed reads as errors: rejected because a valid ref can lack a
  changed path on one side of an added or deleted file.

## References

- Related ADRs: [ADR-0076](0076-host-owned-semantic-diff-git-client.md),
  [ADR-0086](0086-fail-closed-on-git-change-acquisition.md)
- Related docs: [architecture overview](../architecture-overview.md)
