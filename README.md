# roslyn-tools

A CLI for solution-wide C# refactoring and analysis powered by Roslyn. Rename symbols, extract interfaces, inject dependencies, find dead code — all resolved semantically across an entire `.sln`.

## Install

```sh
dotnet tool install -g roslyn-tools-rt
```

Requires .NET 8 SDK or later. After install, `rt` is on your PATH.

## Use with Claude Code

This repo ships a Claude Code plugin so the assistant knows when and how to invoke each command. Install:

```sh
/plugin marketplace add DanMarshall909/roslyn-tools
/plugin install roslyn-tools
```

The plugin contains a single `use-rt` skill that loads on `/use-rt` or any prompt mentioning rt / Roslyn refactoring. The CLI itself (the `rt` binary above) must be installed separately — the skill describes what to invoke, `rt` does the work.

## Feedback & automatic triage

`rt` captures exceptions automatically and lets you log feature requests manually:

```sh
# Captured automatically on any unhandled exception — nothing to do
rt feedback list                      # view pending items
rt feedback list --status all         # view all (including processed)

# Log a feature request or repro case manually
rt feedback add --description "replace-new should support multi-arg drop" \
                --command-line "rt refactor replace-new --type Option<string> ..."
```

Feedback is stored in `ralph/feedback.json`. Items stay pending until triaged.

### Automatic triage via Claude Code cron

Triage runs as a daily Claude Code cron job — no API key, no npm, no manual trigger. Set it up once per machine by pasting this into a Claude Code session for this repo:

```
Schedule a durable daily cron at 8:17am to triage rt feedback: read ralph/feedback.json,
assess each pending item, add genuine bugs and concrete feature requests to
ralph/backlog-prd.json as user stories (using node C:\code\ai-rules\scripts\prd.cjs add),
and mark each item processed with a resolution note.
```

The cron fires once per day while Claude Code is running. It auto-expires after 7 days — re-run the prompt above to renew it.

> **Note:** Cron jobs are session-scoped. They persist across restarts (durable mode) but are tied to the machine where they were created.

## Build from source

```sh
dotnet build RoslynTools.slnx

# Run without installing
dotnet run --project src/RoslynTools.Cli/RoslynTools.Cli.csproj -- <command> [options]

# Publish as single exe (Windows)
dotnet publish src/RoslynTools.Cli/RoslynTools.Cli.csproj -c Release -r win-x64 --self-contained false
```

## Local workflow

Use the local gate before handing off changes:

```sh
# Windows / PowerShell Core
pwsh -NoProfile -File scripts/check-local.ps1

# macOS / Linux
./scripts/check-local.sh
```

For a quick clone sanity check, run `scripts/doctor.ps1` with PowerShell Core or `./scripts/doctor.sh` from bash.

## Commands

Commands fall into three groups:

- **`rt inspect <cmd>`** — read-only, single-symbol or file queries (`describe`, `outline`, `find-refs`, `find-callers`, `find-impl`, `find-unused`, `find-pattern`)
- **`rt analyze <cmd>`** — read-only, solution-wide scans (`circular-deps`, `dependency-graph`, `find-duplicates`, `find-magic-strings`, `list-imports`, `list-smells`, and more)
- **`rt refactor <cmd>`** — mutations (`rename`, `move`, `extract-*`, `inline-method`, `make-*`, `replace-*`, `convert-*`, `add-*`, etc.)

The unqualified form `rt <cmd>` is also accepted for every command.

For the full reference — flags, examples, and output conventions — run:

```sh
rt dump-help
```

Each build writes the output to `docs/COMMANDS.md` (gitignored). The agent-friendly compact reference is deployed automatically to `~/.claude/skills/use-rt/SKILL.md` on every Debug build and is available in Claude Code as `/use-rt`.

---

## Common options

| Option | Description |
|--------|-------------|
| `--solution <path>` | Path to `.sln` or `.slnx` file. Auto-detected if exactly one exists in the current directory. |
| `--dry-run` | Preview changes as a unified diff without writing to disk |
| `--kind <kind>` | Filter by symbol kind: `type`, `method`, `property`, `field`, `namespace` |

## Tests

```sh
dotnet test RoslynTools.slnx -v minimal
```

Each test generates a throwaway C# project, runs Roslyn operations against it, and cleans up automatically.
