# ADR-0044: Typed application catalogue powers MCP

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#54](https://github.com/DanMarshall909/roslyn-tools/issues/54), [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)

## Context

ADR-0043 removed MCP's CLI execution bridge, but MCP discovery and dispatch
still constructed the CLI descriptor registry. That registry builds CLI command
objects to discover option metadata. The typed application definitions already
carry the identity, request type, visibility, safety, and product metadata MCP
needs.

## Decision

Build a reusable catalogue from registered typed application ports. MCP tool
discovery and dispatch read this catalogue, and CLI and MCP startup validate
configured exact MCP names against its public, port-backed entries. The
catalogue is lazy because some application ports depend on the capability
catalogue during service registration. Legacy maintainer ports are excluded.

The CLI descriptor registry remains a separate projection for CLI metadata and
legacy maintainer commands until shared registration and the remaining CLI
projection are migrated. The MCP host still uses CLI `Bootstrap` for DI and
host-specific adapters.

## Consequences

- MCP tool names, schemas, annotations, and dispatch behavior remain based on
  the typed definitions. Tool listing order now follows typed port registration;
  callers must use tool names rather than list positions.
- MCP no longer needs to construct CLI command objects for tool discovery or
  dispatch. The CLI project reference remains until registration and host
  adapters move in a later slice.
- Exact-name configuration validation uses the same typed, public tool set as
  MCP availability, avoiding acceptance of a CLI-only name.

## Alternatives considered

- Keep deriving MCP descriptors from CLI commands: rejected because a host
  adapter would continue to own application metadata and force CLI command
  construction in MCP.
- Move the entire DI registration and CLI descriptor projection at once:
  deferred so this tracer can verify MCP parity before changing the CLI host.
