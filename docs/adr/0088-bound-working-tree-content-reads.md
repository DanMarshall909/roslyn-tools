# ADR-0088: Bound working-tree content reads

- Status: Accepted
- Date: 2026-10-01
- Related issue: [RT-016](../issues/open/architecture/rt-016-pre-release-product-spine.md)
- Extends: [ADR-0087](0087-prove-git-content-absence-before-semantic-diff.md)

## Context

Semantic diff obtains changed working-tree paths from Git, then reads those
paths from the filesystem. `File.Exists` returned false for inaccessible
files as well as absent files. `File.ReadAllText` followed an untracked
`.cs` symlink to content outside the repository. A tracked `.cs` file
replaced by a FIFO could block the reader. These cases could produce an
untrustworthy, unexpectedly broad, or nonterminating comparison.

## Decision

The Hosting Git client accepts only repository-relative working-tree paths
without empty, `.` or `..` components. On Linux x64, it holds a repository
directory handle, opens each path component with `openat` and no-follow flags,
checks mount identity, and reads only a verified regular-file handle.
Nonblocking open prevents a FIFO from hanging the reader before type
verification. Only `ENOENT` proves absence and returns `null`.

Other platforms use path-based preflight: check the root and each existing
component, rejecting symlinks, reparse points, non-directory parents, and
directory leaves. Only an observed missing component returns `null`. Path,
access, and read failures become `GitContentReadException` with `WORKTREE` and
path context, which the shared operation projects as `ExternalToolFailure`.

## Consequences

- On Linux x64, path swaps cannot redirect the opened read through a symlink,
  mounted external tree, FIFO, or other nonregular leaf. An unreadable file is
  an error, not a deletion. The CLI and MCP receive the same typed failure.
- On other platforms, path-based checks still have a concurrent swap race and
  do not verify the leaf's native file type. Do not claim cross-platform
  atomic repository containment or freeze working-tree read effects until
  those platforms have equivalent handle-bound reads or an honest unsupported
  result.
- Hardlink provenance and consistency with separately run Git commands are
  outside this path-bound read guarantee.
- Ref-vs-ref semantic diff and direct two-file comparison keep their existing
  source contracts.

## Alternatives considered

- Keep `File.Exists` as the absence test: rejected because access failures
  can be reported as false.
- Silently omit symlinks: rejected because the comparison would claim a
  complete result while skipping a changed path.
- Assume `FileAttributes` proves a regular file: rejected after a tracked
  `.cs` file replaced by a FIFO passed those checks and blocked the reader.
- Add native read boundaries for every platform in this slice: deferred;
  Linux's existing `openat`/`statx` primitives provide the first bounded
  implementation, while other platforms remain an explicit release gate.

## References

- Related ADRs: [ADR-0076](0076-host-owned-semantic-diff-git-client.md),
  [ADR-0087](0087-prove-git-content-absence-before-semantic-diff.md)
- Related docs: [architecture overview](../architecture-overview.md)
