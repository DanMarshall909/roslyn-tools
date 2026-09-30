# ADR-0081: Bound workspace evidence in CLI inspection results

- Status: Accepted
- Date: 2026-09-30
- Related work: FB-377

## Context

Typed inspection operations carry the complete workspace acquisition account in
their result envelope. A single-file `outline --llm` on this repository emitted
3,943,162 bytes: 5,099 workspace inputs dominated the payload, while 132
diagnostics appeared in both structured and legacy text form. The CLI's
`--llm` flag still serialized that entire envelope as JSON. This exhausted
agent output budgets without improving the answer to a bounded file request.

## Decision

The shared application envelope retains the full acquisition account. When
workspace evidence exceeds the sample limits, the CLI renders a compact
`rt.result.v2` projection of typed inspection envelopes, including for `--json`
and `--llm`. Small envelopes retain their existing `rt.result.v1` shape. The
projection keeps outcome, workspace state,
confidence, limitations, requested and failed projects, exact input and
diagnostic counts, input-state counts, and bounded representative diagnostic
groups. Failed or unknown inputs take priority in the bounded input sample.
The global `--full-workspace-evidence` option retrieves the complete prior
`rt.result.v1` envelope when an audit needs every input and diagnostic.

The projection is pure and belongs to the shared result contract; CLI chooses
when to apply it. MCP continues to receive the complete application result and
can choose its own host projection without changing command semantics.

## Consequences

- Large CLI inspection evidence is bounded by samples rather than solution
  size; exact counts and completeness signals remain visible.
- Consumers that require the former full JSON arrays must opt into
  `--full-workspace-evidence`. The compact schema version is distinct because
  array completeness changed.
- Full retrieval still recomputes the workspace and can be large. A future
  revision-bound evidence paging contract may replace the opt-in full response.

## Alternatives considered

- Truncate diagnostics inside workspace acquisition: rejected because it would
  weaken completeness decisions and every host's ability to audit them.
- Remove workspace evidence entirely: rejected because a `Partial` result
  needs inspectable reasons and exact omission counts.

## References

- Related ADR: [ADR-0007](0007-host-neutral-command-contracts.md)
- Related feedback: `ralph/feedback.json` FB-377
