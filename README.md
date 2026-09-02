# roslyn-tools

`rt` is a semantic C# navigation, analysis, and refactoring tool for developers and coding agents. It uses Roslyn to operate across complete `.sln` and `.slnx` workspaces rather than matching text one file at a time.

Use it to inspect symbols, trace references, analyse architecture, and apply solution-wide refactorings with dry-run previews and machine-readable output. The CLI and MCP host share the same application services and command metadata.

Read the task-oriented documentation at [roslyn-tools.danmarshall.dev](https://roslyn-tools.danmarshall.dev).

## Why `rt`

- **Semantic, solution-wide operations** — symbol resolution, references, implementations, callers, dependencies, and refactorings are based on Roslyn semantics.
- **Safe mutations** — mutating commands support previews and report the changes they intend to make.
- **Agent-friendly discovery** — describe a task in plain English with `rt suggest`, or load a compact session primer with `rt prime`.
- **Structured output** — JSON, YAML, Markdown, and compact LLM-oriented output are available for automation.
- **Multiple hosts** — use the command-line tool directly, install the Claude Code plugin, or run the MCP host from source.

## Requirements

- A C# solution in `.sln` or `.slnx` format
- A compatible .NET SDK when running semantic solution operations

The portable executables include their own runtime and can start without .NET or ICU installed. Roslyn still uses the
consumer solution's SDK to load projects; when a `global.json` is present, install the version it requests.

## Install

Install the portable CLI and MCP host for the current user on POSIX systems:

```sh
curl -fsSL https://raw.githubusercontent.com/DanMarshall909/roslyn-tools/main/scripts/install.sh | bash
```

On Windows PowerShell:

```powershell
irm https://raw.githubusercontent.com/DanMarshall909/roslyn-tools/main/scripts/install.ps1 | iex
```

The installer verifies the release checksum, installs atomically into a versioned user directory, and exposes `rt` and
`roslyn-tools-mcp` through a stable user-scoped launcher. Run a downloaded installer with `--uninstall` (POSIX) or
`-Uninstall` (PowerShell) to remove it.

The .NET tool remains available for CLI-only installations where .NET is already managed centrally:

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
rt inspect describe BookingService --solution MyApp.slnx --trust-repository
```

Request structured output for an agent or script:

```powershell
rt inspect describe BookingService --solution MyApp.slnx --trust-repository --json
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
| Refactor safely | `rename`, `move`, `extract-method`, `delete-unreferenced`, `semantic-diff` |
| Modernise code | `convert-to-record`, `make-async`, `migrate-to-ioptions`, `replace-xunit-asserts` |

Commands are organised into three groups:

- `rt inspect <command>` — read-only symbol and file queries
- `rt analyze <command>` — read-only solution-wide analysis
- `rt refactor <command>` — source mutations

Unambiguous commands are also available directly at the root. These are equivalent:

```powershell
rt inspect describe BookingService --solution MyApp.slnx --trust-repository
rt describe BookingService --solution MyApp.slnx --trust-repository
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
| `--trust-repository` | Permit this CLI invocation to evaluate the reviewed repository |

Strict JSON escaping is the default. Use `--relax-json` only when raw Unicode and
HTML-sensitive characters are preferable to maximum downstream portability.

Commands that need a user choice can use a decisions file:

```powershell
rt refactor rename OldName NewName --solution MyApp.slnx --trust-repository --decisions .rt-decisions.yml
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

The MCP host requires a deliberate, startup-only repository approval before semantic
workspace tools can evaluate MSBuild inputs. Configure the reviewed repository as an
absolute path in the host process arguments:

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

The approval applies only to solutions beneath that root for the lifetime of this MCP
process. A tool request cannot grant trust. Without the startup option, semantic tools
return `TrustRequired` before MSBuild evaluation; non-evaluating discovery remains
available. Configure a separate host entry for each reviewed repository.

The solution also includes the stdio host source in `src/RoslynTools.Mcp`. It exposes the migrated application ports through the same services and command descriptors used by the CLI.

The experimental daemon is disabled by default; CLI and MCP operations run through
their canonical in-process application ports. Set `RT_DAEMON_EXPERIMENT=1` to opt in.
When enabled, a daemon-preferred read-only call starts the sibling `rt` daemon when
needed. It remains warm for ten minutes after the latest request, then exits. Override
the idle period with `RT_DAEMON_IDLE_TIMEOUT_SECONDS`. Manage an explicit resident
instance with `rt maintainer daemon start`, `status`, and `stop`.

Build and run it from the repository root:

```powershell
dotnet build RoslynTools.slnx -c Release
dotnet run --project src/RoslynTools.Mcp/RoslynTools.Mcp.csproj -c Release --no-build -- --trust-repository /absolute/path/to/MyApp
```

Configure an MCP client to launch that command and communicate over stdin/stdout. MCP exposure is being migrated incrementally; use the CLI command catalogue for the complete command surface.

## Feedback capture

Unhandled `rt` exceptions are recorded automatically. Feature requests and reproducible gaps can also be added manually:

```powershell
rt feedback list
rt feedback list --status all

rt feedback add `
  --description "replace-new should support multi-argument drops" `
  --command-line "rt refactor replace-new --type Option<string> ..."

# Preview possible duplicate GitHub issues; this does not write anything.
rt feedback report --id FB-001 --dry-run

# After review, explicitly create a new issue or link an existing one.
rt feedback report --id FB-001 --yes
rt feedback report --id FB-001 --attach-to 123
```

Inside a checkout containing a `ralph` directory, feedback is stored in `ralph/feedback.json`. Elsewhere it falls back to `~/.rt/feedback.json`.

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

Preview and verify the documentation site with:

```sh
npm install
npm run dev
npm test
```

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
