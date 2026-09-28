# ADR-0050: Shared application catalogue and Core operation registration

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#56](https://github.com/DanMarshall909/roslyn-tools/issues/56), [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)

## Context

CLI `Bootstrap` constructed the typed application catalogue and scanned Core
operations, with CLI descriptor registration between those blocks. MCP still
called this CLI method for the reusable application graph.

## Decision

Move the catalogue factory into
`HostServiceRegistration.RegisterApplicationCatalog`, and move product
capabilities, semantic-diff dependencies, and Core `IRtService` scanning into
`HostServiceRegistration.RegisterCoreApplicationServices`. CLI invokes both
methods at their old positions, leaving its descriptor registration between
them. Keep the scanned Core assembly and transient lifetime unchanged. The
Hosting project declares its direct Scrutor dependency.

## Consequences

- MCP can compose the typed application catalogue and Core operations without
  loading CLI registrations.
- CLI feedback ports continue to register before catalogue construction, and
  CLI command scanning remains after Core service scanning.
- The CLI suggestion catalogue, feedback operations, daemon client, command
  descriptors, and command builders remain CLI-owned. MCP still depends on CLI
  until it supplies or shares the required suggestion catalogue and has its
  own registration entry point.
- The CLI-only config, trust, and feedback metadata moved to a shared host
  source in the next #56 slice.

## Alternatives considered

- Reorder all registrations into one shared method: rejected because it would
  change the host's current service registration and descriptor sequence.
