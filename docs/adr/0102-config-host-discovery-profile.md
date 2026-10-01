# ADR-0102: Publish config as a host-only product

- Status: Accepted
- Date: 2026-10-02
- Related issue: [RT-016](../../openspec/changes/shape-pre-release-product-spine/tasks.md)

## Context

`rt config` must inspect invalid host configuration before MSBuild startup and
without a loadable solution or trusted repository. The CLI catalogue used the
legacy metadata fallback because this command has no application port. That
fallback incorrectly advertised explicit trust, a semantic workspace, and SDK
prerequisites.

## Decision

Keep `config` CLI-only and store its product profile beside its shared command
identity in `HostCommandMetadata`. The CLI catalogue resolver overlays this
profile on application-port products for structured help and interactive help.
The profile allows local-state and worktree configuration reads, but requires
no repository trust, semantic workspace, or SDK. Do not register an application
port merely to make `config` discoverable: that would also expose a host repair
operation to other hosts. The required resource fields use the catalogue's
default values and the profile explicitly says that CLI configuration
inspection does not yet enforce them.

## Consequences

- Agents can discover and invoke `config` while diagnosing a broken solution or
  invalid host configuration.
- CLI help and suggestions use one command identity and one product profile.
- MCP policy and tool discovery continue to exclude `config`.
- Configuration file size, diagnostic count, timeout, and output bounds remain
  a separate runtime hardening task before those resource values can be treated
  as enforced limits.
