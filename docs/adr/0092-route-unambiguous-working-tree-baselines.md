# ADR-0092: Route unambiguous working-tree baselines through suggestion

- Status: Accepted
- Date: 2026-10-01
- Related issue: [RT-016](../issues/open/architecture/rt-016-pre-release-product-spine.md)
- Extends: [ADR-0090](0090-route-working-tree-review-with-platform-admission.md), [ADR-0091](0091-exact-baseline-semantic-diff.md)

## Context

After semantic diff learned to discover paths from a selected baseline,
`suggest` still rejected every explicit non-HEAD working-tree request. A broad
natural-language guess would risk emitting a HEAD comparison for a different
requested ref, or silently choose one side of a conflicting request.

## Decision

For explicit C# working-tree review on a platform with contained reads, the
shared suggestion service accepts one unquoted, fully consumed Git ref token
after `against`, `since`, `from`, `versus`, `compared with/to`, `relative to`, or
`vs.`. `from my working tree` remains a source phrase, not a baseline. An
explicit `HEAD` keeps the default route. A non-HEAD token becomes `--from` in
the CLI step and `from` in the MCP arguments when an absolute solution path
supplies the repository directory. Both hosts receive the same workflow.

Missing, quoted, conversational, partial, range-like, or multiple comparator
targets remain typed `Unsupported`, with a direct `semantic-diff --from <ref>`
alternative. Git checks whether the selected ref exists when the suggested
command runs. An unstaged-only request remains unsupported because this mode
includes staged changes.

## Consequences

- Common baseline requests such as `since main`, `against v1.0`, and
  `from HEAD~1` yield invokable CLI/MCP steps without executing them.
- The restricted parser deliberately excludes richer Git selectors and free
  wording; callers can choose a ref and invoke the direct command.
- The stop condition still distinguishes declaration deltas from proof of
  unchanged behavior, and Git/content-read failures remain stopping errors.

## References

- [Architecture overview](../architecture-overview.md)
- [Product-spine OpenSpec](../../openspec/changes/shape-pre-release-product-spine/specs/product-spine/spec.md)
