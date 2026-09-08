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

After reviewing the repository, pass its absolute root as startup-only host
configuration:

```json
{
  "mcpServers": {
    "roslyn-tools": {
      "command": "roslyn-tools-mcp",
      "args": ["--trust-repository", "/absolute/path/to/MyApp"]
    }
  }
}
```

The client starts the server over stdio. Protocol messages stay on stdout; diagnostics and progress are kept separate.

This approval applies only to solutions beneath the configured root and lasts only for
that server process. MCP tool arguments cannot grant or broaden trust. Without the
startup option, semantic tools return `TrustRequired` before MSBuild evaluation. Use a
separate configured server entry for each reviewed repository.

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

The available tools depend on the host's startup repository approval. An approved host advertises eligible refactoring tools as well as read-only tools; refactor requests default to dry-run unless the caller explicitly applies them. Without startup approval, discovery is limited to the read-only subset, and semantic workspace tools still require trust before evaluation.

Use the MCP client's tool listing for the current agent surface. MCP exposure is being migrated incrementally; the `rt` CLI command catalogue covers the complete command surface.

## Bound agent output

Prefer focused symbol identities, explicit solution paths, and structured formats. Large source and relationship results carry limits or continuation information so an agent can distinguish a complete result from a bounded projection.

Continue with [semantic inspection](/docs/inspecting-code/) or learn how to [discover agent workflows](/docs/discovery/).
