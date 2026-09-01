---
title: Getting started
description: Install Roslyn Tools, point it at a C# solution, and complete a semantic inspection in a few commands.
order: 1
section: Start here
---

Roslyn Tools is a command-line tool and MCP host for semantic C# work. It loads the same project and solution model that the compiler uses, so its answers can account for symbols, overloads, references, and project boundaries.

## Requirements

Semantic operations need a C# solution in `.sln` or `.slnx` format and a compatible .NET SDK for that solution. Portable releases include the runtime needed to start `rt`, but Roslyn still uses the consumer solution's SDK to load projects.

When a repository has a `global.json`, install the SDK version it requests.

## Install the CLI

Install the portable CLI and MCP host for the current user on macOS or Linux:

```sh
curl -fsSL https://raw.githubusercontent.com/DanMarshall909/roslyn-tools/main/scripts/install.sh | bash
```

On Windows PowerShell:

```powershell
irm https://raw.githubusercontent.com/DanMarshall909/roslyn-tools/main/scripts/install.ps1 | iex
```

If .NET is already managed centrally, install the CLI as a .NET tool:

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
rt inspect describe BookingService --solution MyApp.slnx
```

Request structured output for a script or agent:

```powershell
rt inspect describe BookingService --solution MyApp.slnx --json
```

If the current directory contains exactly one solution, `--solution` can usually be omitted.

## Check the environment

Use `doctor` when solution selection, SDK loading, repository trust, or output limits are unclear:

```powershell
rt inspect doctor --solution MyApp.slnx
```

The result distinguishes a healthy workspace from partial or failed loading rather than treating warnings as complete semantic evidence.

Next, learn how to [discover commands and workflows](/docs/discovery/).
