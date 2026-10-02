# roslyn-tools

`rt` brings Roslyn-powered C# tooling to three use cases: developer workflows in the CLI, coding agents through MCP, and rule enforcement in CI. It operates across complete `.sln` and `.slnx` workspaces with compiler-backed understanding of your code.

Inspect symbols, trace references, analyse architecture, preview solution-wide refactorings, and enforce checked-in rules. All three use cases share the same application services and command metadata.

Read the task-oriented documentation at [roslyn-tools.danmarshall.dev](https://roslyn-tools.danmarshall.dev).

## Three ways to use `rt`

| Use case | What it helps you do | Start here |
|---|---|---|
| **Developer CLI** | Explore C# solutions, analyse impact, and preview or apply refactorings from your terminal. | [CLI quick start](#quick-start) |
| **Coding agents through MCP** | Give agents semantic tools with structured results and explicit repository trust. | [Connect the MCP host](#mcp-host) |
| **CI rule enforcement** | Fail a pipeline when architecture or complexity rules are violated. | [Run CI checks](#ci-rule-enforcement) |

IDE integration is planned as a future fourth use case; it is not available yet.

## Why `rt`

- **Semantic, solution-wide operations** — symbol resolution, references, implementations, callers, dependencies, and refactorings are based on Roslyn semantics.
- **Safe mutations** — mutating commands support previews and report the changes they intend to make.
- **Agent-friendly discovery** — describe a task in plain English with `rt suggest`, or load a compact session primer with `rt prime`.
- **Structured output** — JSON, YAML, Markdown, and compact LLM-oriented output are available for automation.
- **Shared tooling** — use the CLI locally, connect an MCP client, or run configured checks in your CI pipeline.

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
rt suggest "find every caller of a method" --top 3 --llm
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

MCP clients invoke `prime` with `{}` and `suggest_command` with
`{"query":"find callers of a method","top":3}`. Both work before workspace
selection or repository trust. Use the selected command's help or MCP input
schema for its actual arguments and apply controls.
Raw `tools/list` responses contain at most 20 tools. `prime` and
`suggest_command` appear on the first page; follow `nextCursor` to inspect the
complete catalogue and its schemas.

Agents can start with [the use-rt skill](skills/use-rt/SKILL.md). The compact
path is `prime` → `suggest --top 3 --llm` → scoped `--help`; use
`dump-help --compact` for a broader catalogue. This keeps discovery bounded
while preserving warnings about incomplete semantic evidence.

## Core workflows

| Goal | Representative commands |
|---|---|
| Understand code | `describe`, `outline`, `source` |
| Navigate relationships | `relationships` |
| Analyse a solution | `find-unused`, `list-smells`, `complexity`, `dependency-graph`, `circular-deps` |
| Refactor safely | `rename`, `move`, `extract-method`, `delete-unreferenced`, `semantic-diff` |
| Modernise code | `convert-to-record`, `make-async`, `migrate-to-ioptions`, `replace-xunit-asserts` |
| Enforce repository rules | `ci check` |

Commands are organised into four groups:

- `rt inspect <command>` — read-only symbol and file queries
- `rt analyze <command>` — read-only solution-wide analysis
- `rt refactor <command>` — source mutations
- `rt ci check` — enforce configured rules with a failing exit status; see [CI setup](docs/ci.md).

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
rt ci --help
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
| `--full-workspace-evidence` | Include every workspace input and diagnostic in typed inspection output |
| `--no-suggest` | Suppress suggestion tails on not-found errors |
| `--relax-json` | Use relaxed JSON escaping instead of the strict default |
| `--trust-repository` | Permit this CLI invocation to evaluate the reviewed repository |

Strict JSON escaping is the default. Use `--relax-json` only when raw Unicode and
HTML-sensitive characters are preferable to maximum downstream portability.
Large typed inspection results use a compact `rt.result.v2` workspace summary
in JSON and LLM output. Use `--full-workspace-evidence` to retrieve the
complete `rt.result.v1` workspace account when diagnosing a partial result.

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

### Codex and portable agent plugin

This repository includes a preview [Agent Plugins 1.0](https://agent-plugins.org/)
bundle under `plugins/roslyn-tools` and a repository marketplace at
`.agents/plugins/marketplace.json`. The bundle starts the installed MCP host with the
portable `roslyn-tools-mcp` command; it carries no checkout path, trust argument, or
machine-specific launcher path. Install the portable CLI and MCP host before installing
the plugin.

The portable MCP schema does not currently encode Codex-specific `required` or
`startup_timeout_sec` settings. Plugin installation therefore owns the portable stdio
transport, while fail-hard startup enforcement and a custom timeout remain client policy
when a client exposes those settings.

Persist trust after reviewing the repository. Linked task worktrees share the same Git
common-directory identity, so this is normally a one-time decision:

```powershell
rt trust add /absolute/path/to/MyApp
rt trust status /absolute/path/to/MyApp
```

Then configure one stable MCP launcher without a worktree-bound argument:

```json
{
  "mcpServers": {
    "roslyn-tools": {
      "command": "roslyn-tools-mcp"
    }
  }
}
```

Each semantic request is checked against the persistent registry and Git's current
registered-worktree inventory before MSBuild evaluation. A tool request cannot grant or
broaden trust, and trust-management operations are never exposed through MCP. The old
`--trust-repository <absolute-root>` startup option remains available as an
invocation-scoped compatibility path.

The solution also includes the stdio host source in `src/RoslynTools.Mcp`. It exposes the migrated application ports through the same services and command descriptors used by the CLI.

### Host configuration

CLI and MCP hosts read optional JSON configuration from `~/.rt/config.json` and from
`.rt/config.json` at the current Git worktree root. Repository daemon values override
global values; the existing daemon environment variables override both. MCP allowlists
intersect across the two files, denylists accumulate, and denials win.

```json
{
  "schemaVersion": 1,
  "daemon": {
    "enabled": true,
    "autoStart": false,
    "idleTimeoutSeconds": 600
  },
  "mcp": {
    "enabledTools": ["outline", "source", "relationships"],
    "disabledTools": ["source"]
  }
}
```

Both files use a strict schema. Inspect selected paths, effective settings, and value
sources with `rt config --json`; this command also reports an invalid file without
starting MSBuild or the daemon. Configuration can narrow the MCP surface but cannot
grant repository trust or hide CLI commands.

The experimental daemon is disabled by default; CLI and MCP operations run through
their canonical in-process application ports. Set `daemon.enabled` or
`RT_DAEMON_EXPERIMENT=1` to opt in. When enabled, a daemon-preferred read-only call
starts the sibling `rt` daemon when needed unless `daemon.autoStart` or
`RT_DAEMON_AUTO_START` disables lazy startup. It remains warm for ten minutes after the
latest request by default; use `daemon.idleTimeoutSeconds` or
`RT_DAEMON_IDLE_TIMEOUT_SECONDS` to change that period. Manage an explicit resident
instance with `rt maintainer daemon start`, `status`, and `stop`.

Build and run it from the repository root:

```powershell
dotnet build RoslynTools.slnx -c Release
dotnet run --project src/RoslynTools.Mcp/RoslynTools.Mcp.csproj -c Release --no-build
```

Configure an MCP client to launch that command and communicate over stdin/stdout. A host
with at least one valid persistent record advertises eligible refactoring tools as well as
read-only tools; every call remains target-gated and refactor requests remain dry-run by
default unless the caller explicitly applies them. With no valid record, discovery is limited
to the read-only subset. MCP exposure is being migrated incrementally; use the CLI command
catalogue for the complete command surface.

## CI rule enforcement

Check in a `.rt/ci.json` policy beside your solution. For example, limit method cyclomatic complexity to 10:

```json
{
  "version": 1,
  "rules": [
    { "id": "maintainability.complexity", "maxCyclomatic": 10 }
  ]
}
```

After installing `rt`, setting up your solution's SDK, and restoring dependencies, run:

```sh
rt ci check --solution MyApp.slnx --config .rt/ci.json --json --trust-repository
```

Exit code `0` means every configured rule passed; violations return `1` with structured findings. Invalid policies, compilation errors, and incomplete workspace loading also fail the command. Use explicit trust only for repository code approved for evaluation by that pipeline.

Add architecture rules alongside complexity checks to enforce project and dependency boundaries. See the [CI guide](docs/ci.md) for policy options and a GitHub Actions example.

## Feedback capture

Unhandled `rt` exceptions are recorded automatically. Feature requests and reproducible gaps can also be added manually:

```powershell
rt maintainer feedback list
rt maintainer feedback list --status all

rt maintainer feedback add `
  --description "replace-new should support multi-argument drops" `
  --command-line "rt refactor replace-new --type Option<string> ..."

# Preview possible duplicate GitHub issues; this does not write anything.
rt maintainer feedback report --id FB-001 --dry-run

# After review, explicitly create a new issue or link an existing one.
rt maintainer feedback report --id FB-001 --yes
rt maintainer feedback report --id FB-001 --attach-to 123
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

The Unix gate requires Python 3 to validate test reports. Use `--full` (or
`-Full` in PowerShell) to run every test project in the solution. Each test
project must produce a fresh, completed, nonempty TRX report before the gate
can pass, even if `dotnet test` exits successfully. The gate prints the report
path for each project and keeps reports when verification fails. Pass
`--results-directory <path>` (or `-ResultsDirectory <path>`) to retain reports
after a successful run.

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
