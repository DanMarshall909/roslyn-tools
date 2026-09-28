# ADR-0059: Host configuration belongs to shared Hosting

- Status: Accepted
- Date: 2026-09-28
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0020](0020-layered-host-configuration.md) and [ADR-0058](0058-daemon-transport-hosting-ownership.md)

## Context

CLI and MCP startup share strict configuration loading and validation. The
loader reads user and worktree files and process environment variables; MCP
policy validates configured tool names against the public host descriptor
surface. These implementations remained in Core after daemon transport moved
to Hosting. Project-reference tests could not detect host code already compiled
into the reusable application assembly.

## Decision

Place the configuration loader, effective host settings, and MCP descriptor
validator in shared Hosting. Preserve their namespaces, type shapes, defaults,
and validation order so CLI, MCP, and daemon startup retain the same behavior.
Extend the architecture test to require these types in Hosting and ensure their
old Core source locations do not return.

## Consequences

- Core no longer reads or validates host-specific configuration.
- Host adapters consume one configuration implementation without referencing
  one another.
- This placement change does not alter config schema, precedence, CLI output,
  or MCP discovery and dispatch policy.
- Legacy ambient console output in Core and services remains a separate
  host-neutrality audit before #53 can close.
