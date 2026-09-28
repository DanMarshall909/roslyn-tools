# ADR-0074: MCP solution write reports stay out of the protocol stream

- Status: Accepted
- Date: 2026-09-29
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0073](0073-host-owned-ambient-console.md)

## Context

Shared Hosting composes a console sink for legacy solution write reports.
MCP also uses that composition, so a writer report could appear on process
stdout and corrupt the JSON-RPC stream. Current typed mutation operations
return structured preview and apply results; they do not use the legacy
report lines as their result contract.

## Decision

Replace `ISolutionWriteOutput` with the Core no-op sink in MCP host
composition. Keep the Hosting console sink for CLI and daemon composition.
An MCP composition test invokes all three report methods under a captured
console and requires that no process output is produced.

## Consequences

- MCP solution write reports cannot escape into its JSON-RPC process stream.
- Typed MCP operation results remain in structured tool responses.
- An operation that needs additional report data in MCP must add it to its
  typed result rather than relying on ambient write reports.
