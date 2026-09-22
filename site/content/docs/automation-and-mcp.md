---
title: Connect with MCP
description: Connect an AI coding agent to compiler-backed C# navigation and analysis through the Roslyn Tools MCP server.
order: 1
section: Coding agents
---

Connect the Roslyn Tools MCP host to give coding agents bounded, structured semantic tools for C# repositories. It shares its Roslyn-powered engine with the [developer CLI](/docs/getting-started/) and [CI rule checks](/docs/ci/).

## Install the MCP server

Install the portable MCP server and companion CLI for the current user on macOS or Linux:

```sh
curl -fsSL https://raw.githubusercontent.com/DanMarshall909/roslyn-tools/main/scripts/install.sh | bash
```

On Windows PowerShell:

```powershell
irm https://raw.githubusercontent.com/DanMarshall909/roslyn-tools/main/scripts/install.ps1 | iex
```

The installer exposes the `roslyn-tools-mcp` launcher. Semantic operations still need a compatible .NET SDK for the C# solution being inspected.

## Configure your MCP client

After reviewing the repository, persist trust once:

```powershell
rt trust add /absolute/path/to/MyApp
```

Then configure one stable launcher. New registered worktrees for the same Git common
directory do not require client configuration changes:

```json
{
  "mcpServers": {
    "roslyn-tools": {
      "command": "roslyn-tools-mcp"
    }
  }
}
```

The client starts the server over stdio. Protocol messages stay on stdout; diagnostics and progress are kept separate.

## Install as a portable agent plugin

The source repository also publishes a preview Agent Plugins 1.0 bundle in
`plugins/roslyn-tools`, listed by `.agents/plugins/marketplace.json`. Its portable MCP
configuration launches the bare `roslyn-tools-mcp` command with no arguments, trust path,
or machine-specific location. Install the portable Roslyn Tools launchers first, then add
the repository marketplace through your agent client's plugin workflow.

Agent Plugins 1.0 does not currently define Codex-specific `required` or
`startup_timeout_sec` fields. The bundle therefore cannot require startup success or set a
longer catalogue timeout portably; those remain client-side settings when supported.

Each call revalidates its solution against the persistent record and Git's current
registered-worktree inventory. MCP tool arguments cannot grant or broaden trust, and
trust-management operations are not MCP tools. `--trust-repository <absolute-root>`
remains available as a startup-scoped compatibility option.

## Configure host behavior and tool policy

The CLI and MCP host read optional strict JSON configuration from
`~/.rt/config.json` and from `.rt/config.json` at the current Git worktree root.
Repository daemon values override global values. Existing daemon environment variables
override both files.

```json
{
  "schemaVersion": 1,
  "daemon": {
    "enabled": true,
    "autoStart": false,
    "idleTimeoutSeconds": 600
  },
  "mcp": {
    "enabledTools": ["outline", "source", "relationships"],
    "disabledTools": ["source"]
  }
}
```

MCP allowlists intersect across selected files, denylists accumulate, and denial wins.
Omitting `enabledTools` leaves that layer unrestricted; an empty array exposes no MCP
tools. Names are exact and validated against the public MCP catalogue during startup.
The same policy controls discovery and invocation. It does not hide CLI commands or
grant repository trust.

Run `rt config --json` to inspect the selected files, effective values, and value
sources. The command remains available when a file is invalid and does not start
MSBuild or route through the daemon.

## Give agents semantic tools

Once connected, an agent can discover focused operations such as:

| Tool | Use |
| --- | --- |
| `search_symbols` | Resolve typed symbol candidates before acting |
| `describe` and `source` | Inspect declarations and bounded related source |
| `relationships` | Follow references, callers, and implementations |
| `dependency_graph` and `circular_deps` | Analyze solution structure |
| `suggest_command` | Find the right Roslyn Tools workflow from a goal |

These tools return typed results with explicit workspace context. That lets an agent distinguish semantic evidence from an incomplete load or a bounded response.

## Respect the exposed surface

The available tools depend on current host authority. A host with startup trust or at
least one live persistent record advertises eligible refactoring tools as well as
read-only tools; each call still validates its own repository, and refactor requests
default to dry-run unless the caller explicitly applies them. With no live record,
discovery is limited to the read-only subset.

Use the MCP client's tool listing for the current agent surface. MCP exposure is being migrated incrementally; the `rt` CLI command catalogue covers the complete command surface.

## Bound agent output

Prefer focused symbol identities, explicit solution paths, and structured formats. Large source and relationship results carry limits or continuation information so an agent can distinguish a complete result from a bounded projection.

Continue with [semantic inspection](/docs/inspecting-code/) or learn how to [discover agent workflows](/docs/discovery/).
