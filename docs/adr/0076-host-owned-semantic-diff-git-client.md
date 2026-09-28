# ADR-0076: Semantic diff obtains git clients from shared hosting

- Status: Accepted
- Date: 2026-09-29
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0075](0075-cli-owned-github-issue-process-adapter.md)

## Context

Semantic diff can compare the current repository or a directory supplied in
the request. Its real `GitClient` shells the `git` executable. Although shared
host composition supplied the default client, the reusable
`SemanticDiffOperation` constructed a concrete process client itself for an
explicit directory.

## Decision

Define `IGitClientFactory` beside the reusable `IGitClient` contract. The
operation asks that factory for a client when the request names a directory.
Shared Hosting owns the concrete factory and the unchanged `GitClient`
implementation, and supplies the default current-directory client through
the same factory. Keep internal namespaces stable. Lock placement and the
explicit-directory factory path with tests.

## Consequences

- Reusable Services no longer constructs or contains the git process adapter.
- CLI and MCP use the same host implementation for default and explicit
  working directories.
- Existing git command arguments, output handling, and semantic diff results
  remain unchanged.
- Tests can provide an in-memory client factory for an explicit directory
  without starting a process.
