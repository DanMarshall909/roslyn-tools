# ADR-0049: Shared application infrastructure registration

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#56](https://github.com/DanMarshall909/roslyn-tools/issues/56), [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)

## Context

CLI `Bootstrap` still registered reusable daemon RPC, symbol/document
resolution, and prepared-change writing services. MCP received them by
calling CLI registration, though all concrete types in this block are in Core.

## Decision

Move the block with its existing order, lifetimes, and factories to
`HostServiceRegistration.RegisterApplicationInfrastructure` in
`RoslynTools.Hosting`. CLI invokes it at the same point, after its CLI-only
daemon client and before the suggestion and feedback registrations. A test
checks the key shared service descriptors.

## Consequences

- MCP can register this application infrastructure without CLI code.
- The optional daemon SDK guard remains a host binding resolved from DI at
  runtime. The shared registration does not depend on the CLI guard type.
- Application catalogue construction and Core service scanning moved into
  Hosting in the next #56 slice. Suggestion catalogue and CLI feedback/command
  wiring remain in CLI `Bootstrap`.

## Alternatives considered

- Move all remaining registration together: deferred because CLI-only feedback
  and suggestion wiring still sits between shared registration blocks.
