# ADR-0005: Daemon-centred local execution

- Status: Superseded in part by [ADR-0008](0008-daemon-transparent-passthrough-phase.md)
- Date: 2026-08-16
- Stories: rt-012

## Context

`rt` already has a background daemon that owns a warm process, a named-pipe
transport, lifecycle state, logging, and workspace initialisation.  Its present
contract is intentionally narrow: the CLI forwards an array of command-line
arguments and receives rendered stdout/stderr frames.  The MCP host starts a
separate process and resolves the same services in its own DI container.

That leaves two independently hosted execution paths and makes a transparent
upgrade of a persistent MCP connection impossible.  Replacing an executable
behind a stdio MCP connection can affect only newly spawned processes.  It
would also be a poor boundary for an IDE integration to parse CLI text merely
to call an application operation.

## Decision

The daemon is the stable local execution host for Roslyn Tools.  CLI, MCP, and
future IDE integrations are thin transport adapters over one typed local-RPC
contract.  The daemon owns workspace lifetime, command execution,
cancellation, structured diagnostics, and upgrade readiness.

The RPC contract is tool-agnostic: it carries a command descriptor identity,
validated typed arguments, correlation and cancellation data, and structured
success or failure results.  It must not carry CLI argument arrays, rendered
terminal output, or MCP protocol messages.

The MCP executable remains a stable stdio bridge.  It translates MCP requests
to local-RPC calls and maintains the client-facing connection while it
reconnects to a replaced daemon.  CLI and IDE adapters perform equivalent
translation for their native input/output concerns.  A narrow in-process path
remains for daemon lifecycle, bootstrap diagnostics, and recovery when the
daemon is unavailable.

## Consequences

- One process owns the reusable Roslyn workspace cache and command execution.
- A daemon can be upgraded independently of an established MCP stdio session.
- New transports reuse the typed RPC contract rather than treating rendered
  CLI output as an API.
- Command descriptors remain the single registration source; transport
  adapters do not acquire command-specific business logic.
- The current CLI-argument/stdio daemon protocol is a migration compatibility
  path, not the target extension point.
- The rollout needs request draining, explicit retry/idempotency rules,
  readiness checks, cancellation propagation, and spawned integration tests
  before hot replacement is advertised as reliable.

## Alternatives considered

- Replace the MCP executable in place: rejected because an existing stdio
  connection remains bound to the old process.
- Add a second MCP-specific supervisor: rejected because it duplicates daemon
  lifecycle and workspace ownership.
- Use the current daemon protocol as the MCP backend: rejected because CLI
  arguments and rendered stdout/stderr are not a durable application boundary.
- Put MCP protocol handling in the daemon: rejected because MCP remains an
  edge transport and would couple every future host to its lifecycle.

## References

- Commits: pending
- Related ADRs: [ADR-0001](0001-core-service-thin-host-adapters.md), [ADR-0003](0003-shared-command-descriptors-and-application-ports.md), [ADR-0004](0004-portable-mcp-distribution.md), [ADR-0008](0008-daemon-transparent-passthrough-phase.md)
- Related docs: [architecture overview](../architecture-overview.md), [rt-012](../issues/open/architecture/rt-012-portable-mcp-installation-and-distribution.md)
