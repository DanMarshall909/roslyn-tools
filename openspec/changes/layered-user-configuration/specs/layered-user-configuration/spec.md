## ADDED Requirements

### Requirement: Hosts resolve versioned global and worktree configuration
The system SHALL read optional schema-version-1 configuration from
`~/.rt/config.json` and from `.rt/config.json` at the resolved Git worktree root.

#### Scenario: repository values override global daemon values
- **WHEN** the global and repository files provide different daemon scalar values
- **THEN** the repository values are effective for invocations from that worktree

#### Scenario: another directory cannot contribute local configuration
- **WHEN** an invocation is outside Git or a parent outside the worktree contains `.rt/config.json`
- **THEN** that file is not loaded as repository configuration

#### Scenario: invalid configuration fails closed
- **WHEN** either selected file is malformed, uses an unsupported schema version, contains unknown members, or has invalid values
- **THEN** normal CLI and MCP startup fails with the exact file path and an actionable error

#### Scenario: configuration remains diagnosable
- **WHEN** selected configuration is invalid
- **THEN** `rt config` reports the failure without starting MSBuild or routing through the daemon

### Requirement: Precedence is deterministic and compatible
The system SHALL apply built-in defaults, global configuration, repository
configuration, environment overrides, and explicit command-line overrides in that
order where those layers define a value.

#### Scenario: existing daemon environment overrides files
- **WHEN** a daemon environment variable and a selected file define the same setting
- **THEN** the environment value is effective

#### Scenario: absent files preserve existing behavior
- **WHEN** neither configuration file exists and daemon environment variables are absent
- **THEN** daemon routing is disabled, lazy auto-start defaults to enabled once routing is enabled, idle timeout is ten minutes, and no additional MCP tool filter applies

### Requirement: Daemon lifecycle is configurable without changing authority
The system SHALL configure daemon routing, lazy auto-start, and lazy-start idle timeout
from the effective settings while leaving manual lifecycle commands available.

#### Scenario: enabled daemon may route
- **WHEN** `daemon.enabled` is true and a call is daemon-eligible
- **THEN** the host may use a reachable daemon under the existing placement and recovery rules

#### Scenario: disabled auto-start does not spawn
- **WHEN** daemon routing is enabled, `daemon.autoStart` is false, and no daemon is reachable before dispatch
- **THEN** the request falls back in-process without launching a daemon

#### Scenario: manual lifecycle remains available
- **WHEN** daemon routing or auto-start is disabled
- **THEN** `rt maintainer daemon start`, `status`, `logs`, and `stop` remain local control-plane commands

### Requirement: MCP tool policy is exact, layered, and consistent
The system SHALL use exact MCP tool names, intersect configured allowlists, accumulate
configured denylists, and apply denial after allowance to both discovery and dispatch.

#### Scenario: omitted and empty allowlists differ
- **WHEN** every selected `enabledTools` value is omitted
- **THEN** all otherwise eligible public MCP tools remain allowed
- **AND WHEN** a selected `enabledTools` value is an empty array
- **THEN** no MCP tool is available

#### Scenario: repository policy can only narrow global policy
- **WHEN** global and repository allowlists are both present or either layer denies a tool
- **THEN** only their intersection minus the union of denials remains available

#### Scenario: discovery and invocation agree
- **WHEN** a tool is excluded by effective policy
- **THEN** it is absent from `tools/list` and direct invocation returns the same unknown-tool boundary as an unavailable tool

#### Scenario: unknown configured tool fails startup
- **WHEN** a selected allowlist or denylist names no public MCP descriptor
- **THEN** MCP startup fails with the configuration file and unknown exact name

### Requirement: Configuration does not grant trust or alter CLI command availability
The system SHALL keep repository trust and ordinary CLI command discovery outside the
configuration authority.

#### Scenario: local configuration cannot grant repository trust
- **WHEN** a repository file enables daemon routing or includes a repository-write MCP tool
- **THEN** existing persistent or invocation-scoped trust checks still control discovery and execution

#### Scenario: MCP filtering does not hide CLI commands
- **WHEN** an MCP tool is denied by configuration
- **THEN** the corresponding CLI command remains available under its existing CLI safety rules
