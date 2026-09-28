# ADR-0053: MCP-owned host composition

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#56](https://github.com/DanMarshall909/roslyn-tools/issues/56), [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)

## Context

MCP discovery and dispatch already used typed application ports, but the MCP
project still referenced the CLI assembly to call `Bootstrap.RegisterServices`.
The shared registrations and suggestion catalogue now live in Hosting, so the
runtime no longer needs CLI composition.

## Decision

The MCP host composes shared foundation, application infrastructure, suggestion
catalogue, typed application catalogue, and Core operations before adding its
protocol services. `Program` invokes this MCP-owned entry point. Remove the
MCP-to-CLI project reference and the duplicate daemon registrations from the
protocol-specific registration method.

## Consequences

- MCP has no compile-time dependency on CLI command builders or their parser.
- A project-file test rejects a restored CLI project reference; DI and protocol
  tests exercise MCP-owned composition. The `tools/list` response is byte-identical
  to the prior baseline.
- The test project still references CLI for cross-host comparisons, and its
  general `McpTestHost` fixture still uses CLI bootstrap. Migrating that fixture
  is a separate tracer before #56 closes; neither test dependency is part of the
  MCP runtime graph.

## Alternatives considered

- Keep calling CLI bootstrap through a reflection bridge: rejected because it
  retains the runtime dependency and leaves CLI-only registrations in MCP.
