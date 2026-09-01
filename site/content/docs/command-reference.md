---
title: Command reference
description: Use canonical help, shared options, exit codes, and source-build commands without relying on a stale copied catalogue.
order: 7
section: Reference
---

Roslyn Tools generates command reference data from each command's canonical metadata. Use the installed CLI for the complete, version-matched catalogue.

## Read help

```powershell
rt --help
rt inspect --help
rt analyze --help
rt refactor --help
rt dump-help --compact
```

Run a specific command with `--help` to see its arguments, validation, examples, supported formats, maturity, and effects.

## Select a workspace

Use `--solution <path>` to select a `.sln` or `.slnx`. When exactly one solution is present, `rt` can normally select it automatically. Pass an explicit path in scripts and CI so the request does not depend on the process working directory.

## Handle exit codes

Treat exit codes as part of the command contract:

| Code | Meaning |
| --- | --- |
| `0` | The command completed successfully |
| `1` | The request failed or was invalid |
| `2` | A decisions file contains unresolved choices |

Read structured error output where available rather than matching human-formatted phrases.

## Build from source

```powershell
dotnet restore RoslynTools.slnx
dotnet build RoslynTools.slnx
dotnet run --project src/RoslynTools.Cli/RoslynTools.Cli.csproj -- suggest "find unused code"
```

Run the repository gate before handing off changes:

```sh
bash scripts/check-local.sh
```

Architecture and contributor documentation lives in the [GitHub repository](https://github.com/DanMarshall909/roslyn-tools/tree/main/docs).

## All commands

The catalogue below is generated from the canonical command metadata. It includes every public inspect, analyze, and refactor command in this version of Roslyn Tools.
