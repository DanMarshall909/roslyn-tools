# ADR-0054: CLI application commands enter daemon through typed ports

- Status: Accepted
- Date: 2026-09-28
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)

## Context

CLI startup still had a pre-dispatch branch that forwarded argv to the resident
daemon before building the CLI command tree. Typed commands and mutations were
excluded from that branch, but a legacy read-only or unknown invocation could
still enter the daemon as CLI arguments and execute `App.RunAsync` there.

## Decision

Remove root-level argv forwarding. CLI always binds its own arguments; a typed
command adapter may then use the structured daemon RPC through its application
port. A public read-only command inventory test ensures commands are typed or
explicitly local. Remove the invocation classifier used only to exclude typed
commands from the old pre-dispatch branch.

## Consequences

- Application command routing no longer depends on the CLI argv transport.
- Local CLI help and errors stay with the CLI process. Typed daemon dispatch
  retains its existing recovery and SDK guards.
- The daemon server still accepts the legacy argv envelope for compatibility;
  restricting that receiver and removing its now-unused client path are the
  next tracer before #53's daemon criterion is complete.

## Alternatives considered

- Keep a fallback argv route for commands without typed ports: rejected because
  the public read-only inventory has no such command, while mutations already
  remain local under current safety policy.
