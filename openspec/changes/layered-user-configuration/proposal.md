# Layered user configuration

- Status: accepted
- Decision owner: user

## Why

Daemon routing and idle lifetime are currently controlled only by process environment
variables, while the MCP surface can only be changed in code. Users need durable
machine-wide defaults and repositories need reviewable local policy without teaching
each host a different configuration mechanism.

## What Changes

- Add a versioned global configuration file at `~/.rt/config.json`.
- Add an optional repository configuration file at `<worktree-root>/.rt/config.json`.
- Resolve defaults, global configuration, repository configuration, environment, and
  explicit command-line settings in one typed configuration service.
- Configure daemon enablement, lazy auto-start, and idle timeout through that service.
- Configure the MCP tool surface with exact-name allowlists and denylists, enforced for
  both discovery and invocation.
- Add a local `rt config` command that reports paths, sources, and effective settings and
  can diagnose invalid configuration without starting MSBuild or the daemon.
- Keep repository trust outside both configuration files.

## Capabilities

### New Capabilities

- `layered-user-configuration`: Strict, deterministic global and worktree-local runtime
  configuration for daemon behavior and MCP tool exposure.

### Modified Capabilities

None.

## Impact

The change affects CLI and MCP startup, daemon recovery/launch policy, MCP discovery and
dispatch admission, user documentation, and architecture guidance. It adds no package
dependency and does not let a repository grant trust, change mutation safety, or enable
a tool denied by machine-wide policy.
