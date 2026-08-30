# ADR-0006: MCP lazy daemon lifetime

- Status: Superseded in part by [ADR-0008](0008-daemon-transparent-passthrough-phase.md)
- Date: 2026-08-26
- Stories: daemon RPC recovery

> **Current ruling:** ADR-0008 supersedes the warm-workspace objective and universal
> in-process recovery described below. Lazy startup may support eligible transparent
> typed transport, but the daemon owns no workspace or mutation state. Fallback is
> allowed only after proven `NotDispatched`; an indeterminate mutation is never
> retried or run in-process, and apply-capable commands remain in-process until the
> current hardening gates are separately satisfied.

## Context

Daemon-owned MCP tools use typed local RPC, but a newly installed or restarted
MCP host may have no daemon to contact. Falling straight back to in-process
execution keeps the tool available but loses the warm workspace that the daemon
boundary is intended to provide. Keeping every automatically started daemon
alive indefinitely would instead leave background processes resident after MCP
clients stop using them.

The portable release contains separate `roslyn-tools-mcp` and `rt` executables.
The MCP process therefore cannot restart itself in daemon-server mode.

## Decision

The MCP adapter attempts typed RPC first. If no daemon is reachable, it lazily
starts the sibling `rt` executable in daemon-server mode, waits for readiness,
and retries the same typed RPC once. Source builds may use `dotnet rt.dll` when
the sibling apphost is absent. Spawned stdout and stderr are drained away from
the MCP stdio protocol.

Only an MCP-started daemon receives an idle deadline. The default is ten minutes
and `RT_DAEMON_IDLE_TIMEOUT_SECONDS` may override it. Completing any request
starts a fresh idle period. A daemon started explicitly with `rt daemon start`
has no idle deadline and remains resident until stopped.

Concurrent MCP hosts may race to start the daemon. The named-pipe owner wins;
other hosts recover by observing that pipe during readiness polling. If launch
or retry fails, the existing typed in-process recovery remains available.

## Consequences

- The first daemon-owned MCP call pays one daemon startup cost; later calls use
  the resident workspace during the idle window.
- Portable installs rely on `rt` and `roslyn-tools-mcp` remaining siblings in
  the release directory, which the existing combined archive guarantees.
- Manual daemon lifecycle remains an explicit, indefinitely resident mode.
- The timeout environment variable provides a narrow installation and test
  configuration surface without adding MCP protocol fields.
- MCP availability does not depend on successful process startup because the
  in-process fallback remains fail-safe.

## Alternatives considered

- Always execute in-process when the daemon is absent: rejected because MCP
  never gains the warm-workspace benefit without separate user setup.
- Start the MCP executable with daemon arguments: rejected because MCP and `rt`
  are distinct executables with different entry points.
- Give every daemon the idle timeout: rejected because it changes the explicit
  `rt daemon start` lifecycle contract.
- Keep MCP-started daemons forever: rejected because transient MCP use would
  accumulate resident background processes.

## References

- Commits: pending
- Related ADRs: [ADR-0004](0004-portable-mcp-distribution.md), [ADR-0005](0005-daemon-centred-local-execution.md), [ADR-0008](0008-daemon-transparent-passthrough-phase.md)
- Related docs: [architecture overview](../architecture-overview.md)
