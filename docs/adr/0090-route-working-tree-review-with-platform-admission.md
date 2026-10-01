# ADR-0090: Route working-tree review with platform admission

- Status: Accepted
- Date: 2026-10-01
- Related issue: [RT-016](../issues/open/architecture/rt-016-pre-release-product-spine.md)
- Extends: [ADR-0002](0002-suggestions-output-contract-and-proactive-session-start.md), [ADR-0089](0089-honest-working-tree-read-platform-support.md)

## Context

`suggest "review my working tree C# changes"` ranked unrelated commands instead
of the available semantic-diff operation. Its generic first example also used
working-tree mode, which is unavailable outside Linux x64. Discovery must not
recommend a call that its own Git reader will refuse on the current host.

## Decision

The shared suggestion service recognizes explicit working-tree C# review intent.
On Linux x64, it returns one `semantic-diff --json` CLI step. An absolute solution
path supplies the repository directory to CLI and an invokable `semantic_diff`
MCP step; without that path, the response explains why MCP arguments are not
available. The MCP adapter admits an absolute `directory` for the Git-backed
semantic-diff request as its trust target. The workflow names Git/content-read
errors and the declaration-only scope as stop conditions. Suggestion does not
run the step.

The default comparison is HEAD against current files, including staged changes.
An unstaged-only request returns typed `Unsupported`. An explicit non-HEAD
working-tree baseline also returns typed `Unsupported`: the operation reads
before content at `--from`, but its changed-path list still comes from HEAD,
so a clean tree can omit every change since that baseline. Cross-platform
ref-vs-ref comparison remains available, and help now states this limitation.

The non-HEAD path limitation was removed by [ADR-0091](0091-exact-baseline-semantic-diff.md).
`semantic-diff --from <ref>` now compares that ref with the working tree.
Task-aware suggestion still returns `Unsupported` for explicit baselines until
it can parse them safely, and points callers to the direct command.
That routing limitation was narrowed by [ADR-0092](0092-route-unambiguous-working-tree-baselines.md):
one unambiguous ref now becomes `--from` in the suggested workflow.

The Git client and suggestion service use one platform capability predicate.
On unsupported hosts, or if the command is absent from the catalogue, the
suggestion is typed `Unsupported` with the missing capability
`semantic-diff-working-tree` and no invokable call. The platform reason points
to ref-vs-ref and two-file alternatives. The generic semantic-diff catalogue
example now leads with ref-vs-ref mode, which remains available across hosts.

## Consequences

- CLI and MCP project the same task-aware decision from the shared service.
- The route is deliberately limited to explicit working-tree C# review language;
  general semantic classification of Git context and metadata-generated
  workflows remain open in RT-016.
- A solution path identifies a target directory, not proof of repository trust
  or workspace completeness. The selected command performs its own admission.
- Persistent MCP requests with a semantic-diff directory use the same absolute
  target and repository trust gate as solution-targeted requests.

## References

- [Architecture overview](../architecture-overview.md)
- [Product-spine OpenSpec](../../openspec/changes/shape-pre-release-product-spine/specs/product-spine/spec.md)
