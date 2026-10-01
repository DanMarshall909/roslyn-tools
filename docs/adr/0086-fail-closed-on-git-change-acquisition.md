# ADR-0086: Fail closed on Git change acquisition

- Status: Accepted
- Date: 2026-10-01
- Related issue: [RT-016](../issues/open/architecture/rt-016-pre-release-product-spine.md)
- Extends: [ADR-0076](0076-host-owned-semantic-diff-git-client.md)

## Context

Omitted-query CLI `suggest` derives outline calls from paths changed against
`HEAD`. It caught every Git error and displayed zero changed C# files. The
shared Git client also ignored failed `git diff` and `git ls-files` exits, so a
non-repository directory, unborn `HEAD`, or failed command could look clean.
The same client lists changed paths for semantic diff.

## Decision

Changed-path acquisition throws with the failed command, working directory,
exit status, and a bounded Git diagnostic when a command fails. The process
adapter drains stdout and stderr concurrently so a full diagnostic pipe cannot
block acquisition. Process startup failure is also an error. Omitted-query CLI `suggest` reports the unavailable Git
context on stderr with a nonzero exit, including in JSON mode, and tells the
caller to supply a query or use a repository with a commit. Explicit-query
suggestions do not read Git context.

## Consequences

- An empty changed-path list means the Git commands succeeded; unavailable
  context cannot be mistaken for a clean working tree.
- Direct between-ref acquisition also fails when a ref is invalid instead of
  silently returning no changes.
- Content-at-ref reads still return `null` for a path absent at a valid ref.
  The current `git show` adapter also maps other nonzero exits to `null`; that
  separate semantic-diff ambiguity remains to be resolved.
  MCP omitted-query parity remains a separate request-sensitive trust and
  effects decision.

## Alternatives considered

- Return an empty list after Git failure: rejected because it makes failed
  acquisition indistinguishable from a clean repository.
- Add an MCP Git fallback in this slice: deferred until it can declare an
  explicit repository selector and conditional trust/effects contract.

## References

- Related ADRs: [ADR-0076](0076-host-owned-semantic-diff-git-client.md),
  [ADR-0083](0083-honest-unsupported-plan-suggestions.md)
