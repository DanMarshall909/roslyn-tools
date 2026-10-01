# ADR-0089: Report unsupported working-tree read platforms

- Status: Accepted
- Date: 2026-10-01
- Related issue: [RT-016](../issues/open/architecture/rt-016-pre-release-product-spine.md)
- Supersedes the non-Linux fallback in: [ADR-0088](0088-bound-working-tree-content-reads.md)

## Context

ADR-0088 added a handle-bound working-tree read path on Linux x64, while other
platforms retained path-based preflight. Preflight can race a symlink swap and
cannot prove a regular leaf before opening it; a tracked `.cs` path replaced
with a FIFO demonstrated how a native special file could block the reader.
The preview requires truthful platform read effects.

## Decision

Git-backed working-tree semantic diff reads content only on Linux x64, where
the held-handle, no-follow, same-mount, regular-file boundary is available.
On other platforms, the Hosting Git client fails before working-tree path
discovery, including a clean tree, with a contextual
`GitContentReadException`. The shared operation returns
`ExternalToolFailure` with an explicit unsupported-platform message.
Ref-vs-ref Git comparison and direct two-file comparison remain available.

## Consequences

- A missing safe reader cannot silently fall back to a path-based read or
  claim complete repository-contained evidence.
- Working-tree semantic diff is temporarily unavailable on Windows, macOS,
  and Linux architectures other than x64. The error directs callers to the
  supported ref-vs-ref and two-file modes.
- Native contained readers can be added per platform without changing the
  host-neutral source or operation contract. Discovery must describe the
  mode-specific availability when it recommends semantic diff.

## Alternatives considered

- Keep path-based preflight as a supported read mode: rejected because it
  cannot establish stable repository containment or regular-file type.
- Disable all semantic diff modes on unsupported platforms: rejected because
  ref-vs-ref and direct two-file modes do not use this working-tree reader.

## References

- Related ADRs: [ADR-0076](0076-host-owned-semantic-diff-git-client.md),
  [ADR-0087](0087-prove-git-content-absence-before-semantic-diff.md),
  [ADR-0088](0088-bound-working-tree-content-reads.md)
- Related docs: [architecture overview](../architecture-overview.md)
