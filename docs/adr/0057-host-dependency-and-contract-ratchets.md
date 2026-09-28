# ADR-0057: Host dependency and typed-contract ratchets

- Status: Accepted
- Date: 2026-09-28
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)

## Context

The CLI, MCP, and daemon now compose typed application ports independently.
Existing tests checked individual boundaries, but they did not enforce the
whole production project graph or compare the three host compositions. A future
project reference, parser package, or divergent registration could silently
reintroduce host coupling into the reusable application layer.

## Decision

Test the complete production project graph declared by the solution: Core has
no project reference; Hosting depends only on Core; MCP and DaemonHost depend
only on Core and Hosting; CLI depends on Core, Hosting, and DaemonHost. Reject
CLI parser and terminal-renderer packages outside CLI, and check compiled
assembly references for forbidden host dependencies. Compare the typed
application descriptors composed by CLI, MCP, and daemon by canonical name,
request and response types, safety policy, and MCP visibility. A new production
host or deliberate graph change must update this test and its architecture
decision.

## Consequences

- Core/Application cannot gain a project or compiled assembly dependency on a
  host adapter without failing the architecture test.
- Shared Hosting cannot gain a CLI, MCP, or daemon host dependency. CLI-only
  parser and terminal dependencies stay at the CLI edge.
- Divergence in the typed application catalogue across the three current hosts
  fails the same test. Host-only controls remain outside that catalogue.
- The ratchet governs assembly dependencies and typed contracts. It does not
  claim that every internal helper in Core is itself an application operation;
  existing daemon RPC transport helpers remain in Core. Their relocation and a
  source-placement guard are required before #53's final criterion is complete.

## Alternatives considered

- Rely on conventions and individual host tests: rejected because neither
  detects a newly added project edge or a registration gap across all hosts.
