---
name: use-rt
description: "Reference guide for the rt Roslyn CLI tool — solution-wide C# rename, refactor, navigation, and analysis. Load this when performing any C# refactoring or symbol-navigation task. Triggers on: /use-rt, use rt, roslyn refactoring, rt command."
user-invocable: true
---

# rt — Roslyn CLI Reference

`rt` is a Roslyn-powered C# refactoring CLI. It performs solution-wide refactoring (rename, move, extract-*, find-refs, etc.) using semantic analysis instead of text matching.

> **Install:** `dotnet tool install -g roslyn-tools-rt` (requires .NET 8 SDK or later). After install, `rt` is on your PATH.

## Command groups

Every command lives under one of three top-level groups. Either form works:

```
rt <group> <command> [args...]   # canonical, e.g. rt refactor extract-method ...
rt <command> [args...]           # root shortcut — works for every command name
```

| Group | Purpose |
|---|---|
| `rt inspect` | Read-only: examine a single symbol or file (`describe`, `outline`, `find-refs`, `find-callers`, `find-impl`, `find-unused`, `find-pattern`) |
| `rt analyze` | Read-only: scan for cross-cutting issues (`circular-deps`, `dependency-graph`, `find-duplicates`, `find-magic-strings`, `list-imports`, `list-smells`, and more) |
| `rt refactor` | Mutations (`rename`, `move`, `extract-*`, `inline-method`, `make-*`, `replace-*`, `convert-*`, `add-*`, etc.) |

## Full command reference

The authoritative, always-current reference is the binary itself:

```sh
rt dump-help
```

This also writes `docs/COMMANDS.md` on each build (gitignored). Use it for flags, examples, aliases, and output format for any specific command.

## Solution targeting

Most commands need `--solution <path>`. If omitted, `rt` walks up from the current directory looking for a single `.sln` / `.slnx`. Pass an explicit path when there's ambiguity or when invoking from outside the repo.

## Output conventions

- **Read-only commands** (describe, outline, find-impl, find-duplicates, find-pattern, dependency-graph): JSON to stdout
- **Mutation commands** (rename, move, extract-*, etc.): plain text diff to stdout
- **All commands**: diagnostics and progress to stderr; results to stdout
- **Exit codes**: `0` success, `1` error, `2` decisions required (see `--decisions <path.yaml>`)

## Rules

- Always pass `--dry-run` first for any mutation command, confirm the diff looks correct, then re-run without it
- All commands support `--dry-run`
- Never block on a failed `rt` call — fall back to manual edits and continue
