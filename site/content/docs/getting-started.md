---
title: Use the CLI
description: Use the rt command-line interface to inspect C# solutions, analyze impact, and preview or apply reviewed refactoring work.
order: 2
section: Developer CLI
---

Use the `rt` CLI to explore C# solutions, analyze impact, and preview or apply refactorings. It shares its semantic engine with the [MCP host for coding agents](/docs/automation-and-mcp/) and [CI rule checks](/docs/ci/).

## Requirements

Semantic operations need a C# solution in `.sln` or `.slnx` format and a compatible .NET SDK for that solution. Portable releases include the runtime needed to start `rt`, but Roslyn still uses the consumer solution's SDK to load projects.

When a repository has a `global.json`, install the SDK version it requests.

## Install Roslyn Tools

Install the portable MCP server and CLI for the current user on macOS or Linux:

```sh
curl -fsSL https://raw.githubusercontent.com/DanMarshall909/roslyn-tools/main/scripts/install.sh | bash
```

On Windows PowerShell:

```powershell
irm https://raw.githubusercontent.com/DanMarshall909/roslyn-tools/main/scripts/install.ps1 | iex
```

If .NET is already managed centrally and only the CLI is needed, install it as a .NET tool:

```powershell
dotnet tool install --global roslyn-tools-rt
```

## Start with a goal

You do not need to memorize the command catalogue. Ask `rt` which workflow fits the work:

```powershell
rt suggest "find every caller of a method"
```

Inspect a type semantically:

```powershell
rt inspect describe BookingService --solution MyApp.slnx --trust-repository
```

Request structured output for a script or agent:

```powershell
rt inspect describe BookingService --solution MyApp.slnx --trust-repository --json
```

If the current directory contains exactly one solution, `--solution` can usually be omitted.

## Check the environment

Use `doctor` when solution selection, SDK loading, repository trust, or output limits are unclear:

```powershell
rt inspect doctor --solution MyApp.slnx --trust-repository
```

The result distinguishes a healthy workspace from partial or failed loading rather than treating warnings as complete semantic evidence.

`--trust-repository` is a deliberate, invocation-scoped approval. For a reviewed
repository used repeatedly by the MCP host or daemon, use `rt trust add <repository>`;
linked worktrees share that persistent Git common-directory decision. Review the
repository first because MSBuild workspace loading may execute repository-controlled
build logic.

Next, learn how to [discover commands and workflows](/docs/discovery/).
