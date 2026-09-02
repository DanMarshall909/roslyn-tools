---
title: Connect with MCP
description: Connect an AI coding agent to compiler-backed C# navigation and analysis through the Roslyn Tools MCP server.
order: 1
section: Start here
---

Roslyn Tools is primarily an MCP server for AI coding agents working in C# repositories. It gives an agent bounded, structured tools backed by the same solution model the compiler uses—not text matches or terminal output it has to scrape.

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

MCP is the primary agent surface, but its stable tool list is intentionally focused. Normal discovery currently excludes apply-capable repository mutations and maintainer plumbing, so an agent can inspect and plan without silently writing to the repository.

Use the MCP client's tool listing for the agent surface. Use the companion `rt` CLI when a reviewed change is ready to preview or apply.

## Bound agent output

Prefer focused symbol identities, explicit solution paths, and structured formats. Large source and relationship results carry limits or continuation information so an agent can distinguish a complete result from a bounded projection.

Continue with [semantic inspection](/docs/inspecting-code/) or learn how to [discover agent workflows](/docs/discovery/).
