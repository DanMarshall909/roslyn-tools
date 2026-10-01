# ADR-0083: Return honest unsupported outcomes for unavailable plan tasks

- Status: Accepted
- Date: 2026-10-01
- Related OpenSpec: [pre-release product spine](../../openspec/changes/shape-pre-release-product-spine/proposal.md)

## Context

The shared `suggest` service ranks command names, usage text, and examples by
token overlap. After the incomplete durable Prepare primitive left public
discovery, a request to verify a prepared plan still ranked `doctor`,
`add-dependency`, and `find-unused`. A request for an immutable rename plan
could rank the current rename mutator. None produces or verifies a revision-bound
change plan. A score threshold alone cannot distinguish those task requirements
from incidental overlap with words such as “verify” or “rename”.

## Decision

Give the shared suggestion response an explicit `Recommendations`, `NoMatch`, or
`Unsupported` outcome. Before lexical ranking, identify task language that asks
to prepare or verify a change plan and return `Unsupported` with the missing
`prepare-plan` or `verify-plan` capability, no command candidates, and no related
commands. CLI JSON and MCP structured content carry the same semantic result;
CLI text states the missing capability. An unsupported *task* remains a successful
discovery invocation, not an `RtError` or MCP protocol failure.

This is a narrow truthfulness guard. It does not claim a general relevance
threshold, full intent understanding, or an ordered workflow planner.

## Consequences

- Recognized requests for plan preparation or verification cannot silently
  route agents to an unrelated inspection or apply-capable mutation. The
  classifier is intentionally bounded; other phrasing still requires the
  general workflow relevance gate.
- Existing supported command queries retain ranked results.
- Callers can distinguish a lexical miss from a known unavailable capability.
- Future workflow discovery can replace the narrow classifier while preserving
  the explicit outcome contract.

## Alternatives considered

- **Raise one BM25 score threshold.** Rejected because an unrelated command can
  receive a high score from a shared word without fulfilling the requested task.
- **Return an operation error.** Rejected because discovery ran successfully;
  the requested capability is absent.
- **Offer the current dry-run as a prepared plan.** Rejected because it does not
  provide an immutable, revision-bound plan with verification authority.

## References

- [Surface audit](../../openspec/changes/shape-pre-release-product-spine/pre-freeze-surface-audit.md)
- [Architecture overview](../architecture-overview.md)
