# Configuration

`rt` reads two optional JSON files when a CLI or MCP process starts:

1. `~/.rt/config.json` for user-wide defaults.
2. `<git-worktree-root>/.rt/config.json` for repository-local settings.

Use [`schemas/rt-config-v1.schema.json`](schemas/rt-config-v1.schema.json) for editor
validation. A repository file can be checked in when the team wants a shared, reduced
MCP surface. Repository trust is stored separately and cannot be granted by either file.

```json
{
  "version": 1,
  "daemon": {
    "enabled": true,
    "autoStart": true,
    "idleTimeoutSeconds": 600
  },
  "mcp": {
    "enabledTools": ["prime", "outline", "source", "relationships"],
    "disabledTools": ["rename"]
  }
}
```

The `$schema` member is intentionally not part of the runtime contract because unknown
members fail validation. Configure file-to-schema mapping in the editor rather than
adding `$schema` to the runtime file.

## Precedence

Daemon scalar values resolve in this order, with the last defined value winning:

1. built-in defaults;
2. global configuration;
3. repository configuration;
4. `RT_DAEMON_EXPERIMENT` or `RT_DAEMON_IDLE_TIMEOUT_SECONDS`;
5. an explicit host option, where available.

MCP lists compose more restrictively:

- omitted `enabledTools` means unrestricted at that layer;
- an empty `enabledTools` array means no tools;
- multiple allowlists are intersected;
- all `disabledTools` values are combined;
- denial wins over allowance.

Tool names are exact MCP names such as `search_symbols`, not CLI spellings such as
`search-symbols`. The MCP host refuses to start when a selected file names an unknown
public tool. Tool policy affects MCP discovery and invocation only; CLI commands retain
their normal availability and safety rules.

## Daemon settings

`daemon.enabled` opts eligible read-only calls into daemon routing. `daemon.autoStart`
controls whether a missing daemon may be started lazily; when false, an already-running
daemon may still be used and a proven pre-dispatch miss falls back in-process.
`daemon.idleTimeoutSeconds` must be greater than zero and no more than 86,400 seconds.

Manual lifecycle commands are always local and remain available:

```powershell
rt maintainer daemon start
rt maintainer daemon status
rt maintainer daemon logs
rt maintainer daemon stop
```

## Inspect and repair

Run `rt config` for a human-readable summary or `rt config --json` for structured output.
The command does not register MSBuild or route through the daemon, and it reports the
exact invalid file even when normal command startup is blocked.

Configuration is read at process startup. Restart a long-running MCP host or daemon after
changing a selected file.
