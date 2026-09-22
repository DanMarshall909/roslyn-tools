# ADR-0018: Bounded external semantic review

- Status: Accepted
- Date: 2026-09-18
- Stories: GitHub #135

## Context

Deterministic Roslyn rules efficiently find broad SOLID candidates but cannot reliably
distinguish design violations from composition roots, closed syntax classifications,
cohesive wide protocols, or other architectural exceptions. Sending an entire trusted
repository to a model would create an opaque disclosure boundary, and treating model
output as proof would weaken deterministic evidence and failure semantics.

CLI and MCP must expose the same operation. Repository trust covers evaluation of the
checkout but does not by itself communicate consent to transmit source externally.

## Decision

Add a provider-neutral, in-process `solid-review` application operation after the raw
`solid` analysis. Preview is the default. Live review requires a distinct
`--transmit-source` option in addition to repository trust.

The operation preserves raw results and prepares stable, bounded candidates. Each
external request contains one sanitized declaration plus only principle-relevant
structural evidence. The provider returns a typed three-way advisory choice, independent
probabilities for meaningful design harm and an accepted exception, and a typed
principle-specific exception reason. Application code composes those signals with
conservative thresholds: weak or contradictory evidence abstains to `uncertain`, and a
false-positive classification must expose the accepted exception that supports it.
Provider failures remain in-band beside the raw findings; partial and failed live
execution is nonzero at the CLI boundary.

Comments and literal contents are removed before transmission, and source is explicitly
identified as untrusted data. V1 has no cache. The output identity includes provider,
requested/resolved model, versioned question set, request ID, repository revision, and
source/evidence hash. Incomplete ISP evidence cannot be promoted to a likely violation
from interface size alone.

The CLI is a thin adapter. Generic MCP projection uses the same typed port and only
advertises this explicit-trust external-write operation when startup or persistent
repository trust exists. Request payloads cannot grant trust.

## Consequences

- Deterministic coverage remains inspectable and unchanged while semantic labels improve
  review ordering.
- Users can see candidate symbols, source byte counts, and estimated requests before any
  network call.
- Source disclosure is bounded but not anonymous; identifiers and code structure remain
  visible to the provider.
- One request per candidate gives clear identity and failure scope at the cost of latency.
- Model drift and calibration remain human-review concerns, so there is no CI enforcement.
- Diagnostic signals make likely false positives explainable, but the initial thresholds
  remain provisional until measured against a labelled live corpus.
- Absence of caching avoids persisted source and stale judgments but repeats cost.
- Provider adapters can be replaced without changing host surfaces or candidate policy.

## Alternatives considered

- Upload the repository once: rejected because it broadens disclosure and obscures
  candidate-level provenance and failure scope.
- Let semantic output replace raw findings: rejected because advisory inference must not
  suppress deterministic evidence.
- Make review automatic after `solid`: rejected because repository trust is not external
  transmission consent.
- Batch candidates: deferred because V1 prioritizes bounded source identity and isolated
  failures over throughput.
- Cache responses immediately: deferred until cost, retention, invalidation, and drift
  behavior are measured.

## References

- Related ADRs: [ADR-0003](0003-shared-command-descriptors-and-application-ports.md),
  [ADR-0007](0007-host-neutral-command-contracts.md),
  [ADR-0011](0011-authoritative-trust-and-evaluator-isolation.md)
- Related docs: [SOLID semantic review](../solid-semantic-review.md),
  [V1 evaluation](../evaluations/solid-semantic-review-v1.md)
- Related issues: GitHub #135, GitHub #136
