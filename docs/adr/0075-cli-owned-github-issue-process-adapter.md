# ADR-0075: The GitHub issue process adapter belongs to CLI

- Status: Accepted
- Date: 2026-09-29
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0074](0074-mcp-solution-report-boundary.md)

## Context

The feedback command is registered only by the CLI, but its concrete
`GitHubIssueClient` implementation lived in reusable Services and launched
the authenticated `gh` executable. The service contract and issue models are
useful for testing feedback operations without invoking that process.

## Decision

Move the concrete `GitHubIssueClient` byte-identically to the CLI assembly.
Retain its internal namespace and `IGitHubIssueClient` contract so feedback
composition and tests need no behavior change. Lock assembly placement with
an architecture test.

## Consequences

- Reusable Services no longer contains the `gh` process adapter.
- CLI feedback keeps the same authentication, issue commands, error handling,
  and temporary body-file behavior.
- The service contract stays reusable; only CLI composition selects the real
  process implementation.
