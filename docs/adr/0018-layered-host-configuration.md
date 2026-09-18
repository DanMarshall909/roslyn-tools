# ADR-0018: Layered host configuration

- Status: Accepted
- Date: 2026-09-18
- Related OpenSpec: [layered user configuration](../../openspec/changes/layered-user-configuration/proposal.md)

## Context

Daemon routing and idle lifetime were process-environment concerns, while MCP tool
exposure was fixed by the descriptor registry and repository trust state. This made
durable user preferences awkward and gave a repository no reviewable way to reduce the
agent-facing surface it intended to use.

Configuration is not authority. In particular, a file from repository content must
not grant that repository trust or silently override a machine-wide decision to hide a
tool. Discovery and invocation also cannot consult different policies: advertising a
tool that dispatch rejects, or dispatching a hidden tool by name, would make the MCP
boundary misleading.

## Decision

Hosts load optional schema-version-1 JSON from `~/.rt/config.json` and from
`.rt/config.json` at the Git worktree root selected for the invocation. They apply
built-in defaults, global values, repository values, existing environment overrides,
and explicit command-line values in increasing precedence. Files reject unknown
members, unsupported versions, invalid values, blank tool names, and duplicate names.
Normal startup fails closed and identifies the offending file. The local `rt config`
command bypasses MSBuild and daemon routing so the effective state remains diagnosable.

Daemon scalars use ordinary overlay semantics. `enabled` admits existing daemon routing,
`autoStart` admits lazy launch after a proven pre-dispatch miss, and
`idleTimeoutSeconds` controls the lazy or manually launched daemon lifetime. Manual
lifecycle commands remain available regardless of these values. The existing
`RT_DAEMON_EXPERIMENT` and `RT_DAEMON_IDLE_TIMEOUT_SECONDS` variables remain higher
precedence compatibility controls.

MCP tool policies compose monotonically. Every configured `enabledTools` allowlist is
intersected; an omitted allowlist is unrestricted at that layer and an empty allowlist
allows no tools. Every `disabledTools` denylist is unioned, and denial wins. Exact names
are validated against public MCP descriptors during MCP startup. The resulting predicate
is the single gate used by both `tools/list` and dispatch.

Repository trust remains a separate registry or invocation-scoped host option. Neither
configuration file can add trust, bypass workspace admission, change dry-run defaults,
or affect ordinary CLI command discovery.

## Consequences

- One user file can enable and tune the daemon for every repository.
- A checked-in worktree file can choose a smaller MCP surface and override daemon scalar
  preferences for that worktree.
- A repository cannot re-enable a globally denied tool or grant itself evaluation trust.
- Invalid configuration stops normal CLI/MCP startup instead of reverting to a broader
  implicit surface; `rt config` remains the repair path.
- Long-running hosts read configuration at startup. Changes require restarting the host
  or starting a new CLI process.

## Alternatives considered

- Environment variables only: rejected because they are hard to inspect, share, and
  review, and do not model MCP policy.
- Repository values fully replace global policy: rejected because untrusted content
  could widen a machine-wide tool restriction.
- Filter discovery only: rejected because a caller could still invoke a hidden tool by
  exact name.
- Store trust in the same file: rejected because configuration content is not an
  authorization ceremony or protected trust record.

## References

- [Configuration reference](../configuration.md)
- [Architecture overview](../architecture-overview.md)
- [ADR-0008](0008-daemon-transparent-passthrough-phase.md)
- [ADR-0011](0011-authoritative-trust-and-evaluator-isolation.md)
