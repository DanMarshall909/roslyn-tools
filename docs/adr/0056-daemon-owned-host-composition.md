# ADR-0056: Daemon-owned host composition

- Status: Accepted
- Date: 2026-09-28
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)

## Context

ADR-0055 made typed RPC the daemon's only application route, but the daemon
server still lived in the CLI assembly and called the CLI's `Bootstrap` to
register services. That made its service graph depend on command builders and
host-only CLI operations even though the receiver no longer used them.

## Decision

Place the daemon server, wire protocol, state, idle lifetime, and SDK guard in
`RoslynTools.DaemonHost`. That assembly references only the reusable Core and
shared Hosting assemblies. It composes foundation, application infrastructure,
suggestion catalogue, application catalogue, and Core operations itself, then
adds the daemon SDK guard. The CLI retains its lifecycle command, stop client,
and published executable entry point; `--daemon-serve` enters the daemon host.

## Consequences

- CLI, MCP, and daemon have independent host composition over the same typed
  application ports. The daemon assembly has no CLI or `System.CommandLine`
  reference.
- The installed CLI tool still carries and launches the daemon in one process
  image, preserving the existing packaging and lifecycle interface.
- CLI-only feedback ports are not registered in the daemon host. The CLI
  lifecycle and feedback commands still execute locally; a direct experimental
  RPC for a legacy `feedback_*` name now returns `command_not_found`.
- An architecture test compares daemon and MCP application catalogues and
  rejects a CLI dependency. A production-composition dispatcher test verifies
  both shared typed port resolution and the CLI-only feedback boundary. Final
  cross-layer ratchets remain under #53.

## Alternatives considered

- Keep a CLI `Bootstrap` call inside the daemon process: rejected because it
  would retain command surface and host-only service coupling.
- Publish a separate daemon executable now: deferred because the assembly
  boundary establishes independent composition without changing installation
  or process lifecycle contracts in this tracer.
