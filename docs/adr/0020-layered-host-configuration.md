# ADR-0020: Layered host configuration

- Status: Accepted
- Date: 2026-09-23
- Change: `layered-user-configuration`

## Context

Daemon routing and idle lifetime were controlled only through process environment
variables, while the MCP tool surface could be changed only in code. The CLI and MCP
hosts need the same durable settings without making a repository configuration file an
authority source. Repository trust must continue to come only from the trust registry
or an explicit invocation-scoped approval.

Configuration also has to remain diagnosable when it is malformed. Loading MSBuild or
routing through the daemon before reporting a configuration error would make repair
depend on the subsystem whose startup is being configured.

## Decision

CLI and MCP hosts resolve one strict, versioned configuration contract from the current
user's `~/.rt/config.json` and the active Git worktree's `.rt/config.json`. Scalar daemon
values use defaults, global file, repository file, environment, then an applicable
explicit host argument. The local `rt config` inspection command reports the selected
files, effective values, and sources without MSBuild or daemon startup.

MCP policy composes conservatively. Each configured `enabledTools` list narrows the
previous layers by intersection, every `disabledTools` list accumulates by union, and a
denial wins. Omission means unrestricted at that layer; an empty allowlist permits no
tools. Exact configured names are checked against the public MCP descriptor registry at
startup. The shared availability predicate applies the resulting policy to both
`tools/list` and dispatch.

Configuration can select daemon routing, lazy auto-start, and lazy-start idle timeout.
Disabling auto-start still permits an already-running daemon and the manual daemon
lifecycle commands. Configuration does not grant repository trust and does not change
ordinary CLI command availability.

Both files use `schemaVersion: 1` and reject malformed JSON, unknown members,
unsupported versions, invalid values, blank tool names, and duplicate tool names.
Normal CLI and MCP startup fails closed with the responsible file path. `rt config`
remains available as the repair path.

## Consequences

- A machine can define durable host defaults and a repository can narrow them through a
  reviewable local file.
- MCP discovery and invocation cannot disagree about a configured tool policy.
- Existing daemon environment variables remain compatible and override file values.
- A checked-in repository file can disable tools or daemon auto-start for that
  worktree, but it cannot enable a tool denied globally or authorize repository code.
- Configuration is resolved at host startup; hot reload and remote policy are deferred.

## Alternatives considered

- **Environment variables only.** Rejected because they do not provide durable,
  reviewable per-worktree policy and leave MCP filtering without a user contract.
- **Repository settings replace global MCP policy.** Rejected because a repository
  could broaden a machine-wide restriction.
- **Put trust grants in configuration.** Rejected because a repository-controlled file
  cannot be an authority source for evaluating that repository.
- **Search arbitrary parent `.rt` directories.** Rejected because configuration must be
  bound to the resolved Git worktree rather than ambient directory layout.

## References

- Related ADRs: [ADR-0008](0008-daemon-transparent-passthrough-phase.md),
  [ADR-0011](0011-authoritative-trust-and-evaluator-isolation.md)
- Related docs: [architecture overview](../architecture-overview.md),
  [MCP guide](../../site/content/docs/automation-and-mcp.md)
- Specification: [layered user configuration](../../openspec/changes/layered-user-configuration/specs/layered-user-configuration/spec.md)
