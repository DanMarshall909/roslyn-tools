# ADR-0003: Shared command descriptors and application ports

- Status: Accepted
- Date: 2026-07-30
- Stories: FB-002, rt-007
- Later decision: [ADR-0043](0043-mcp-port-only-dispatch.md) removes the temporary bridge.

## Context

The original MCP surface had two paths. Ordinary commands were converted back to CLI
arguments and executed through `App.RunAsync`; selected commands had native
`IMcpHandler` implementations. The first path made MCP a wrapper around another host.
The second duplicated binding, defaults, dispatch, schemas, and output policy.
`OutlineCommand` and `OutlineHandler` independently selected file, directory, glob,
namespace, and regex behavior.

Nested and operational commands were also outside the reflection-discovered catalog,
so feedback tools needed handwritten MCP registrations while daemon and other
host-management commands relied on omission by convention.

## Decision

Every command leaf is represented by an `ApplicationCommandDescriptor`. A descriptor
owns command path, tool identity, user metadata, CLI builder, MCP visibility, safety
policy, structured-result capability, and an optional typed
`ICommandApplicationPort`.

CLI and MCP adapters bind their transport inputs into the same request type and invoke
the same port. The generic MCP adapter serializes the port result directly and enforces
mutation dry-run defaults before invocation. There is no command-specific MCP handler
or override path.

The migration is incremental. Descriptors without a port temporarily enter
`CliCommandBridge`; the bridge is a compatibility boundary, not the extension model.
The first migrated set is `outline`, `describe`, `find-refs`, `rename`,
`semantic-diff`, `suggest`, `feedback_add`, and `feedback_list`. Operational
commands such as daemon, prime, and dump-help do not enter the descriptor registry
unless they explicitly opt into a non-host-specific use case.

## Consequences

- Adding a port-backed ordinary command requires no MCP handler.
- CLI and MCP share request validation, dispatch, defaults, and domain output.
- Nested command leaves can be discovered without pretending they are root CLI commands.
- Architecture tests enforce unique MCP tool names, generated schemas, structured
  output declarations, mutation dry-run defaults, and operational-command visibility.
- Commands still using `CliCommandBridge` remain migration debt tracked by rt-007.
- A port introduces a small typed request/result contract even for simple commands.

## Alternatives considered

- Keep the CLI bridge as the permanent generic adapter: rejected because one host would
  continue invoking and parsing another.
- Add a native MCP handler for every command: rejected because schemas and binding
  behavior would remain duplicated.
- Rewrite every command in one change: rejected because the command surface is large
  and mutation behavior requires incremental equivalence tests.

## References

- Related ADRs: [ADR-0001](0001-core-service-thin-host-adapters.md)
- Related docs: [architecture overview](../architecture-overview.md),
  [target command surface](../target-command-surface-architecture.md)
- Issue: [rt-007](../issues/resolved/architecture/rt-007-shared-command-descriptors.md)
