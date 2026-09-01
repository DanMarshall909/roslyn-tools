---
title: Automation and MCP
description: Consume deterministic output from scripts and expose the same host-neutral operations to coding agents over MCP.
order: 6
section: Integration
---

The CLI and MCP host project shared application operations into different host surfaces. Migrated commands return the same typed semantics instead of reconstructing one host from another host's rendered output.

## Choose an output format

Common global options are available for automation:

| Option | Use |
| --- | --- |
| `--json` | Strict machine-readable JSON |
| `--yaml` | Human-readable structured output |
| `--markdown` | Documentation-friendly rendering |
| `--llm` | Compact output intended for model context |
| `--decisions <file>` | Repeatable ambiguity resolution |

Primary results go to stdout. Diagnostics, warnings, and progress remain separate so a pipeline can consume the result without scraping incidental messages.

Strict JSON escaping is the default. Use `--relax-json` only when raw Unicode and HTML-sensitive characters are more useful than maximum downstream portability.

## Configure the MCP host

The portable installation exposes `roslyn-tools-mcp`:

```json
{
  "mcpServers": {
    "roslyn-tools": {
      "command": "roslyn-tools-mcp"
    }
  }
}
```

The host communicates over stdin and stdout. Protocol messages stay on stdout; diagnostics and progress are written separately.

## Respect the exposed surface

MCP exposure is intentionally narrower than the complete CLI catalogue while operations migrate to typed application ports. Normal tool discovery excludes apply-capable repository mutation and maintainer plumbing.

Use `rt dump-help` for the complete CLI surface and the MCP client's tool listing for the operations exposed through that host. Do not assume that a CLI command automatically has an MCP equivalent.

## Bound agent output

Prefer focused symbol identities, explicit solution paths, and structured formats. Large source and relationship results carry limits or continuation information so an agent can distinguish a complete result from a bounded projection.
