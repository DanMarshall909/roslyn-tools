# roslyn-tools

`rt` is a semantic C# navigation, analysis, and refactoring tool for developers and coding agents. It uses Roslyn to operate across complete `.sln` and `.slnx` workspaces rather than matching text one file at a time.

Use it to inspect symbols, trace references, analyse architecture, and apply solution-wide refactorings with dry-run previews and machine-readable output. The CLI and MCP host share the same application services and command metadata.

## Why `rt`

- **Semantic, solution-wide operations** — symbol resolution, references, implementations, callers, dependencies, and refactorings are based on Roslyn semantics.
- **Safe mutations** — mutating commands support previews and report the changes they intend to make.
- **Agent-friendly discovery** — describe a task in plain English with `rt suggest`, or load a compact session primer with `rt prime`.
- **Structured output** — JSON, YAML, Markdown, and compact LLM-oriented output are available for automation.
- **Multiple hosts** — use the command-line tool directly, install the Claude Code plugin, or run the MCP host from source.

## Requirements

- .NET 10 SDK
- A C# solution in `.sln` or `.slnx` format

The repository pins its SDK through [`global.json`](global.json).

## Install

```powershell
dotnet tool install --global roslyn-tools-rt
```

After installation, `rt` is available on your `PATH`.

Update or remove it with:

```powershell
dotnet tool update --global roslyn-tools-rt
dotnet tool uninstall --global roslyn-tools-rt
```

## Quick start

Ask `rt` which command fits a task:

```powershell
rt suggest "find every caller of a method"
```

Inspect a type semantically:

```powershell
rt inspect describe BookingService --solution MyApp.slnx
```

Request structured output for an agent or script:

```powershell
rt inspect describe BookingService --solution MyApp.slnx --json
```

Discover changed C# files and likely next commands from the current Git working tree:

```powershell
rt suggest
```

Print the compact session primer used by coding agents:

```powershell
rt prime
```

## Core workflows

| Goal | Representative commands |
|---|---|
| Understand code | `describe`, `outline`, `source` |
| Navigate relationships | `find-refs`, `find-callers`, `find-impl` |
| Analyse a solution | `find-unused`, `list-smells`, `complexity`, `dependency-graph`, `circular-deps` |
| Refactor safely | `rename`, `move`, `extract-method`, `safe-delete`, `semantic-diff` |
| Modernise code | `convert-to-record`, `make-async`, `migrate-to-ioptions`, `replace-xunit-asserts` |

Commands are organised into three groups:

- `rt inspect <command>` — read-only symbol and file queries
- `rt analyze <command>` — read-only solution-wide analysis
- `rt refactor <command>` — source mutations

Unambiguous commands are also available directly at the root. These are equivalent:

```powershell
rt inspect describe BookingService --solution MyApp.slnx
rt describe BookingService --solution MyApp.slnx
```

## Command discovery and reference

Use the generated command catalogue rather than relying on a manually maintained list:

```powershell
rt --help
rt inspect --help
rt analyze --help
rt refactor --help
rt dump-help
rt dump-help --compact
rt dump-help --json
rt dump-help --yaml
```

`rt dump-help` is generated from each command's canonical metadata, including its category, aliases, examples, options, and supported output formats.

When an unknown command resembles a known command, `rt` also returns ranked suggestions. For natural-language discovery, use:

```powershell
rt suggest "extract an interface from a class"
```

## Output and automation

The primary result is written to stdout. Diagnostics, warnings, and progress are written separately so scripts can consume structured output reliably.

Common global options:

| Option | Purpose |
|---|---|
| `--json` | Emit JSON |
| `--yaml` | Emit YAML |
| `--markdown` | Emit Markdown |
| `--llm` | Emit compact output intended for LLM consumption |
| `--decisions <file>` | Read and persist disambiguation choices in YAML |
| `--show-workspace-warnings` | Include MSBuild workspace warnings |
| `--no-suggest` | Suppress suggestion tails on not-found errors |
| `--relax-json` | Use relaxed JSON escaping instead of the strict default |

Strict JSON escaping is the default. The legacy `--strict-json` option remains as a deprecated no-op for backward compatibility.

Commands that need a user choice can use a decisions file:

```powershell
rt refactor rename OldName NewName --solution MyApp.slnx --decisions .rt-decisions.yml
```

When unresolved choices remain, `rt` writes them to the file and exits with code `2`. Resolve the `choose:` fields and rerun the same command.

Common command options include:

| Option | Purpose |
|---|---|
| `--solution <path>` | Select a `.sln` or `.slnx`; auto-detected when exactly one is present |
| `--dry-run` | Preview source changes without writing them |
| `--kind <kind>` | Restrict symbol resolution to a kind such as `type`, `method`, `property`, `field`, or `namespace` |

Run a command's `--help` to see its exact capabilities.

## Claude Code plugin

The repository includes a Claude Code plugin containing the `use-rt` skill:

```text
/plugin marketplace add DanMarshall909/roslyn-tools
/plugin install roslyn-tools
```

The CLI must still be installed separately with `dotnet tool install --global roslyn-tools-rt`. The skill teaches Claude Code when and how to invoke `rt`; the CLI performs the work.

## MCP host

The solution includes a stdio MCP host in `src/RoslynTools.Mcp`. It exposes the migrated application ports through the same services and command descriptors used by the CLI.

Build and run it from the repository root:

```powershell
dotnet build RoslynTools.slnx -c Release
dotnet run --project src/RoslynTools.Mcp/RoslynTools.Mcp.csproj -c Release --no-build
```

Configure an MCP client to launch that command and communicate over stdin/stdout. MCP exposure is being migrated incrementally; use the CLI command catalogue for the complete command surface.

## Feedback capture

Unhandled `rt` exceptions are recorded automatically. Feature requests and reproducible gaps can also be added manually. Every report requires a short `--title` and a `--category` (`bug`, `feature`, `question`, or `other`) — consistent, structured reports are what make the duplicate detection in `rt feedback report` (below) work without spamming GitHub:

```powershell
rt feedback list
rt feedback list --status all

rt feedback add `
  --title "replace-new should support multi-argument drops" `
  --category feature `
  --description "replace-new should support multi-argument drops" `
  --command-line "rt refactor replace-new --type Option<string> ..." `
  --command "refactor replace-new"
```

Inside a checkout containing a `ralph` directory, feedback is stored in `ralph/feedback.json`. Elsewhere it falls back to `~/.rt/feedback.json`. This is always local-only — nothing leaves your machine until you explicitly report it.

### Reporting on GitHub

`rt feedback report` files a pending item as a public GitHub issue, using your own `gh` login (install and run `gh auth login` first — no token is ever stored by `rt` itself):

```powershell
rt feedback report --id FB-004
```

It searches existing issues first and, like Stack Overflow's "this looks similar" prompt, shows possible duplicates *before* creating anything. If none of those match, re-run with `--yes` to file a new issue, or `--attach-to <number>` to link your feedback to an existing one instead. Add `--dry-run` to preview without filing or linking anything.

This is a deliberately explicit, human-directed action — separate from `feedback add` — and it is never invoked automatically, including by coding agents driving `rt` over MCP.

Maintainer triage instructions are documented in [`docs/feedback-triage.md`](docs/feedback-triage.md).

## Build from source

```powershell
dotnet restore RoslynTools.slnx
dotnet build RoslynTools.slnx

# Run without installing
dotnet run --project src/RoslynTools.Cli/RoslynTools.Cli.csproj -- suggest "find unused code"
```

Publish a self-contained single-file executable for a target runtime:

```powershell
# Windows x64
dotnet publish src/RoslynTools.Cli/RoslynTools.Cli.csproj -c Release -r win-x64

# Linux x64
dotnet publish src/RoslynTools.Cli/RoslynTools.Cli.csproj -c Release -r linux-x64

# macOS Apple Silicon
dotnet publish src/RoslynTools.Cli/RoslynTools.Cli.csproj -c Release -r osx-arm64
```

RID-specific publishes are configured as self-contained because Roslyn's MSBuild workspace requires its build-host files to be extracted alongside the executable.

## Development

Run the local verification gate before handing off changes:

```powershell
pwsh -NoProfile -File scripts/check-local.ps1
```

On macOS or Linux:

```sh
./scripts/check-local.sh
```

A quick clone sanity check is also available through `scripts/doctor.ps1` or `scripts/doctor.sh`.

Run the standard checks directly with:

```powershell
dotnet format RoslynTools.slnx --verify-no-changes
dotnet build RoslynTools.slnx -c Release
dotnet test RoslynTools.slnx -c Release -v minimal
```

The test suite creates throwaway C# projects, runs Roslyn operations against them, and cleans them up automatically.

## Architecture and contributing

- [`AGENTS.md`](AGENTS.md) — repository-specific guidance for coding agents
- [`docs/architecture-overview.md`](docs/architecture-overview.md) — layers, contracts, output pipeline, and safety model
- [`docs/pattern-core-service.md`](docs/pattern-core-service.md) — the core-service and thin-host-adapter pattern
- [`docs/agent-playbook.md`](docs/agent-playbook.md) — detailed agent workflow
- [`docs/adr/`](docs/adr/) — architecture decision records
