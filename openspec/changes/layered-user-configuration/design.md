## Context

`RT_DAEMON_EXPERIMENT` and `RT_DAEMON_IDLE_TIMEOUT_SECONDS` are the only durable daemon
controls. The MCP adapter already shares one availability predicate between schema
discovery and invocation, which is the correct enforcement seam for tool policy. The
repository trust registry is a separate security authority and must remain separate.

## Goals / Non-Goals

**Goals:**

- One versioned JSON contract used by the CLI and MCP hosts.
- Global defaults plus worktree-local policy resolved without depending on process-wide
  current-directory mutation.
- Strict validation with actionable file-specific errors.
- The same effective MCP set for discovery and invocation.
- Existing environment variables retain compatibility and higher precedence.

**Non-Goals:**

- Repository trust grants, secrets, per-command arguments, remote policy, hot reload, or
  daemon ownership of mutations.
- Changing ordinary CLI command availability.
- Searching parent directories for arbitrary `.rt` folders outside the resolved Git
  worktree root.

## Decisions

### Use two files with one schema

The global file is `~/.rt/config.json`; the local file is
`<worktree-root>/.rt/config.json`. Both use schema version 1 and reject unknown members.
An absent file contributes no values. A malformed, unsupported, or semantically invalid
file fails startup closed, except that `rt config` remains available to diagnose it.

### Resolve the local file from the worktree root

The resolver walks upward from the invocation directory until it finds a `.git` file or
directory, then reads only that root's `.rt/config.json`. Outside Git, no repository file
is loaded. Tests inject paths and file reads rather than changing process current
directory.

### Use safe layered precedence

Built-in defaults are overlaid by global scalar daemon values and then repository scalar
daemon values. Existing environment variables override file values; an explicit host
argument remains highest precedence where one exists.

MCP policies compose rather than replace: configured allowlists intersect, and denylists
accumulate. An omitted allowlist means unrestricted at that layer; an empty allowlist
means no MCP tools. Denial wins. This permits a repository to narrow machine policy but
not silently re-enable a globally denied capability.

### Keep configuration separate from authority

Configuration can control routing and visibility, but it cannot grant repository trust.
Existing persistent or invocation-scoped trust admission still gates repository-write
tool discovery and every workspace request.

### Preserve daemon compatibility while making auto-start explicit

`daemon.enabled` controls whether eligible calls may route through a reachable daemon.
`daemon.autoStart` controls whether recovery may launch a missing daemon; disabling it
still permits use of an already-running daemon and manual lifecycle commands.
`daemon.idleTimeoutSeconds` controls lazy launches. Existing environment variables win
over file values.

## Risks / Trade-offs

- A checked-in local file can change developer behavior. Strict parsing, safe MCP policy
  composition, and the separate trust gate bound that risk.
- Existing tests construct host options directly. Defaults remain behavior-compatible so
  these tests do not depend on a developer's real configuration files.
- A daemon started manually is machine-wide, so its own lifetime cannot vary per request.
  Local idle timeout applies when that repository triggers lazy startup.

## Migration

Existing installations continue with daemon disabled, lazy auto-start enabled when the
daemon is enabled, a ten-minute idle timeout, and the current trust-filtered MCP surface.
Users may move environment-only daemon preferences into the global file incrementally.
