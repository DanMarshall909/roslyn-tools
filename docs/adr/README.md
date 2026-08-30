# Architecture Decision Records

This directory holds the Architecture Decision Records (ADRs) for `rt`. Each ADR captures a single architectural decision: what was chosen, why, what alternatives were rejected, and what the consequences are. The architecture overview at [../architecture-overview.md](../architecture-overview.md) is the running synthesis; ADRs are the historical log.

## When to write an ADR

Write an ADR when a decision:

- introduces or removes a layer contract (e.g. a new capability interface, a renamed registry pattern)
- changes the error-handling doctrine (e.g. typed ladder, Result-vs-throw boundary)
- changes the output pipeline (e.g. renderer collapse, encoder selection, DTO unification)
- adopts or retires a doctrine (e.g. "static classes are migration targets", "rt-first gate on Read/Edit")
- accepts a non-obvious trade-off worth justifying for future readers (e.g. byte-identity gate blocking a refactor)

If the decision only affects a single file or is purely tactical, don't bother — the commit message is enough.

## Format

Use the template below. Files are `NNNN-kebab-case-title.md`, numbered sequentially from `0001`. Once a record is `Accepted`, do not edit it — supersede with a new record.

```markdown
# ADR-NNNN: <title>

- Status: Proposed | Accepted | Deprecated | Superseded by ADR-MMMM
- Date: YYYY-MM-DD (use the system-provided currentDate verbatim)
- Stories: US-NNN, US-MMM (cross-link the work that drove the decision)

## Context

What forced this decision? What was the existing state? What problem appeared?

## Decision

What did we decide? State it plainly — one or two sentences. Then the mechanics:
how the layer, contract, or pipeline works after this decision.

## Consequences

- What gets easier?
- What gets harder?
- What invariants now hold (and where are they enforced — startup check, arch test, code review)?
- What's deliberately deferred (cross-link follow-up stories)?

## Alternatives considered

For each, one line: what it was, why it was rejected.

## References

- Commits: <SHA list>
- Related ADRs: <links>
- Related docs: <links>
```

## When updating after a decision

Per the architecture-overview doc's "Updating this doc" section:

1. Write the ADR here.
2. Update [../architecture-overview.md](../architecture-overview.md) to reflect the new state and cross-link the ADR.
3. Update the most specific companion doc if one applies.
4. Commit all three together as `docs(adr): NNNN <title>`.

## Index

| # | Title | Status | Date |
|---|---|---|---|
| [0001](0001-core-service-thin-host-adapters.md) | Core service + thin host adapters | Accepted | 2026-06-17 |
| [0002](0002-suggestions-output-contract-and-proactive-session-start.md) | _suggestions output contract and proactive session start | Accepted | 2026-06-19 |
| [0003](0003-shared-command-descriptors-and-application-ports.md) | Shared command descriptors and application ports | Accepted | 2026-07-30 |
| [0004](0004-portable-mcp-distribution.md) | Portable MCP distribution | Accepted | 2026-08-16 |
| [0005](0005-daemon-centred-local-execution.md) | Daemon-centred local execution | Superseded in part by [0008](0008-daemon-transparent-passthrough-phase.md) | 2026-08-16 |
| [0006](0006-mcp-lazy-daemon-lifetime.md) | MCP lazy daemon lifetime | Superseded in part by [0008](0008-daemon-transparent-passthrough-phase.md) | 2026-08-26 |
| [0007](0007-host-neutral-command-contracts.md) | Host-neutral command contracts and complete application-port migration | Accepted; daemon boundary updated by [0008](0008-daemon-transparent-passthrough-phase.md) | 2026-08-26 |
| [0008](0008-daemon-transparent-passthrough-phase.md) | Transparent daemon passthrough phase | Superseded in part by [0009](0009-authenticated-daemon-endpoints.md) and [0013](0013-fenced-plan-unavailability-resolution.md) | 2026-08-27 |
| [0009](0009-authenticated-daemon-endpoints.md) | Authenticated local daemon endpoints | Accepted | 2026-08-30 |
| [0010](0010-revision-bound-continuation-tokens.md) | Revision-bound continuation tokens | Accepted | 2026-08-30 |
| [0011](0011-authoritative-trust-and-evaluator-isolation.md) | Authoritative repository trust and evaluator isolation | Accepted | 2026-08-30 |
| [0012](0012-review-first-mutation-defaults.md) | Review-first mutation defaults during hardening | Accepted | 2026-08-30 |
| [0013](0013-fenced-plan-unavailability-resolution.md) | Fenced plan-unavailability resolution | Accepted | 2026-08-30 |
