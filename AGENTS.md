# AGENTS.md

Guidance for agentic coding tools working in this repository.

## Project skills

@.agents/skills/session-log/SKILL.md
@.agents/skills/refactoring-slices/SKILL.md

Use Flo for C# implementation cycles, following the installed `flo` skill and
the [development workflow](docs/agent-playbook.md#development-with-flo).
[flo.json](flo.json) saves the repository defaults. Start with serial cycles,
one accepted criterion at a time; review RED evidence and mutation findings
before integration. Agents already executing a Flo phase should perform that
phase without starting a nested cycle.

We are dogfooding Flo. When a Flo defect or workflow gap appears during RT
work, record a reproducible finding in Flo's tracker and fix it in a separate
Flo Git worktree. Complete Flo's applicable tests and independent review, then
merge and push the verified fix to Flo's `main` as soon as the gates pass.
Refresh the Flo runtime used by RT at a safe cycle boundary, preserving any
active cycle's pinned runtime and evidence.

For C# work, start with [use-rt](skills/use-rt/SKILL.md), then `rt prime` and
`rt suggest "<task>" --top 3 --llm`. Apply
[continuous reflection](docs/agent-playbook.md#continuous-reflection) quietly
throughout the task and aggregate material findings at handoff.

---

## Project overview

- Stack: C# on .NET 10 (`net10.0`).
- SDK: pinned by `global.json`.
- Solution: `RoslynTools.slnx`.
- Main CLI project: `src/RoslynTools.Cli/RoslynTools.Cli.csproj`.
- Core library project: `src/RoslynTools/RoslynTools.csproj`.
- Shared hosting project: `src/RoslynTools.Hosting/RoslynTools.Hosting.csproj`.
- Outer third-party adapters: `src/RoslynTools.Adapters/RoslynTools.Adapters.csproj`.
- MCP host project: `src/RoslynTools.Mcp/RoslynTools.Mcp.csproj`.
- Test projects:
  - `tests/RoslynTools.Tests/RoslynTools.Tests.csproj`
  - `tests/RoslynTools.Tests.Mcp/RoslynTools.Tests.Mcp.csproj`
- CLI framework: `System.CommandLine`.
- Nullable reference types: enabled.
- Implicit usings: enabled.

### Documentation website

- The public documentation site is an Astro static site at `roslyn-tools.danmarshall.dev`.
- Site source lives under `site/`; the existing `docs/` directory remains architecture and project documentation source.
- Astro builds into ignored `artifacts/site/`, and GitHub Pages publishes that output beneath `gh-pages:/docs`.
- Preview locally with `npm run dev`; validate site content, build output, links, and metadata with `npm test`.
- The published branch must contain `docs/index.html`, `docs/CNAME`, `docs/.nojekyll`, and the rest of the generated site beneath `docs/`.
- Do not commit generated site output to `main` or overwrite the repository's authored `docs/` tree.

### Layout

- `src/RoslynTools.Cli/Commands/`: CLI command definitions. Each concrete `*Command.cs` implements `IRtCommand` plus exactly one category marker: `IInspectCommand`, `IAnalyzeCommand`, `IRefactorCommand`, `ICiCommand`, or the root-only `IControlPlaneCommand`. CI commands enforce read-only rule checks with nonzero exit status for violations.
- `src/RoslynTools.Cli/CommandDescriptors/`: the shared application-command descriptor registry used to project command metadata and application ports into host surfaces.
- `src/RoslynTools.Mcp/`: generic MCP descriptor adapter and stdio server. Do not recreate one bespoke MCP handler per CLI command.
- `src/RoslynTools/Services/`: service layer. It must not depend on `System.CommandLine` or render directly to the console. Services receive domain requests and return typed results or errors.
- `src/RoslynTools/Core/`: Roslyn workspace, symbol, change, output, decision, and command-contract primitives.
- `src/RoslynTools/Models/`: small model types.

CLI and MCP should call the same application operation through shared services or typed application ports. Application-port migration is incremental; preserve the compatibility bridge for commands that have not yet migrated.

See [docs/architecture-overview.md](docs/architecture-overview.md) for the full layered design and [docs/adr/0001-core-service-thin-host-adapters.md](docs/adr/0001-core-service-thin-host-adapters.md) for the foundational pattern.

## Command reference

Run `rt dump-help` to view the full command reference, or use:

```powershell
dotnet run --project "src/RoslynTools.Cli/RoslynTools.Cli.csproj" -- dump-help
```

For a compact agent-oriented catalogue:

```powershell
rt dump-help --compact
rt prime
rt suggest "<task in plain English>" --top 3 --llm
```

The canonical command metadata lives in each command's `RtCommandInfo`. Do not hand-maintain a second command catalogue.
Generate the ignored local `docs/COMMANDS.md` explicitly with `npm run generate:commands`.

## Cursor and Copilot rules

Repository-specific rule-file inventory:

- `.cursorrules`: not present
- `.cursor/rules/`: not present
- `.github/copilot-instructions.md`: points Copilot to this guide and the architecture documentation

If additional rule files appear later, treat them as higher-priority repository guidance and merge them with this file.

## Setup

Run from the repository root:

```powershell
dotnet --version
dotnet restore "RoslynTools.slnx"
```

The installed SDK must satisfy `global.json`. Use PowerShell syntax by default on Windows.

## Agent-friendly CLI conventions

- Prefer non-interactive commands and flags suitable for automation.
- Use deterministic output where possible, for example `-v minimal`.
- Quote file paths to avoid shell parsing problems.
- Avoid scripts or commands that require manual prompts.
- Prefer exit-code-driven checks so agents can detect failures reliably.
- Keep command output concise and parseable; diagnostics belong on stderr and primary results on stdout.
- Prefer `--json`, `--yaml`, or `--llm` for automation rather than scraping formatted terminal output.
- Use `--decisions <file>` for repeatable disambiguation instead of interactive selection.
- Use `--dry-run` before a mutating command when the intended change is not already proven by a narrow test.
- Start every refactoring at a clean commit. Record HEAD and full status before preview; require the same HEAD and empty status after dry-run and immediately before apply. If a preview writes files, preserve the evidence and investigate. Test and commit each coherent change before the next refactoring.
- **Parallel-safe by default.** `rt` is hardened for concurrent invocation from parallel agent worktrees: feedback writes, wrapper rebuilds, skill deployment, and isolated MSBuild `obj` directories are protected. See [docs/agent-playbook.md](docs/agent-playbook.md).

## Build commands

Build the entire solution:

```powershell
dotnet build "RoslynTools.slnx"
```

Build Release:

```powershell
dotnet build "RoslynTools.slnx" -c Release
```

Build only the CLI project:

```powershell
dotnet build "src/RoslynTools.Cli/RoslynTools.Cli.csproj"
```

Build only the MCP host:

```powershell
dotnet build "src/RoslynTools.Mcp/RoslynTools.Mcp.csproj"
```

Run CLI help locally:

```powershell
dotnet run --project "src/RoslynTools.Cli/RoslynTools.Cli.csproj" -- --help
```

Run the MCP stdio host locally:

```powershell
dotnet run --project "src/RoslynTools.Mcp/RoslynTools.Mcp.csproj"
```

## Local verification gate

The default local gate runs fast feedback. Suggestion CLI journeys and journaled
apply belong to the slow lane and must never run under mutation testing. Run
`./scripts/check-local.sh --lane slow --configuration Release` once on the final
ticket candidate, or use the complete ticket gate
`./scripts/check-local.sh --full --lane all --configuration Release` instead.
PowerShell uses `-Lane slow` or `-Full -Lane all`. See the
[test-lane workflow](docs/agent-playbook.md#test-lanes-and-ticket-completion).

Prefer the repository gate before handoff:

```powershell
pwsh -NoProfile -File "scripts/check-local.ps1"
```

On macOS or Linux:

```sh
./scripts/check-local.sh
```

Verify that plain restore, Debug build, and test leave authored checkout files
and a disposable user profile unchanged outside declared output/cache paths:

```sh
node --test tests/build-purity.test.mjs
node scripts/check-build-purity.mjs
```

CI runs this purity gate on Linux. Windows and macOS filesystem coverage is
tracked separately in platform hardening P1.8.

Use `scripts/doctor.ps1` or `scripts/doctor.sh` for a quick clone sanity check.

## Lint and formatting

There is no separate lint framework; formatting and analyser enforcement run through the .NET toolchain.

Verify without making edits:

```powershell
dotnet format "RoslynTools.slnx" --verify-no-changes
```

Apply formatting and supported analyser fixes:

```powershell
dotnet format "RoslynTools.slnx"
```

Keep formatting changes limited to task-relevant files unless a broader cleanup is explicitly requested.

## Test commands

Prefer the local driver for routine feedback:

```sh
./scripts/check-local.sh --full --lane fast --configuration Release
./scripts/check-local.sh --filter "FullyQualifiedName~TypeName" --configuration Release
```

The raw `dotnet test` commands below run both lanes when no test filter is supplied.

Run all tests:

```powershell
dotnet test "RoslynTools.slnx" -v minimal
```

Run the CLI/core test project:

```powershell
dotnet test "tests/RoslynTools.Tests/RoslynTools.Tests.csproj" -v minimal
```

Run the MCP test project:

```powershell
dotnet test "tests/RoslynTools.Tests.Mcp/RoslynTools.Tests.Mcp.csproj" -v minimal
```

Run one test by fully qualified name:

```powershell
dotnet test "tests/RoslynTools.Tests/RoslynTools.Tests.csproj" -v minimal --filter "FullyQualifiedName=Namespace.TypeName.TestMethod"
```

Run tests by partial type or method name:

```powershell
dotnet test "tests/RoslynTools.Tests/RoslynTools.Tests.csproj" -v minimal --filter "FullyQualifiedName~TypeName"
```

Useful filter keys depend on the test framework and adapter, but commonly include:

- `FullyQualifiedName`
- `Name`
- `ClassName`
- `Category` or `TestCategory`

Run the narrowest relevant test first, then broaden to the owning project and finally the solution.

## Coding style guidelines

Follow nearby code first. Repository architecture tests enforce many structural rules; do not work around them.

### Imports and namespaces

- Use file-scoped namespaces.
- Keep `using` directives at the file top.
- Order usings as `System.*`, then `Microsoft.*`, then `RoslynTools.*`.
- Remove unused usings.

### Formatting

- Use 4 spaces; no tabs.
- Use Allman braces.
- Prefer one statement per line.
- Wrap long argument lists and fluent chains for readability.

### Types and nullability

- Respect the nullable context and annotate optional references with `?`.
- Validate external input early and return or surface an actionable typed error.
- Prefer `var` when the right-hand side makes the type obvious.
- Use `required` init properties for genuinely required model data where appropriate.

### Async practices

- Prefer async all the way through host adapter, operation, service, and Roslyn APIs.
- Avoid `.Result`, `.Wait()`, and other sync-over-async calls.
- Keep host adapters small and composable.

### Naming

- Types, methods, and properties: PascalCase.
- Locals and parameters: camelCase.
- CLI option variables end with `Opt` when a local option object is still required.
- Use descriptive Roslyn-oriented names such as `solution`, `workspace`, and `symbol`.

### Error handling and output

- Use the repository's typed `Result<T, RtError>` and `RtResult<T>` contracts where the surrounding code has migrated.
- Surface routine CLI errors through `RtErrorPresenter` rather than command-specific output ladders.
- Render command results through `RtResultRenderer`, `RtSerializer`, or the shared output pipeline; do not write result objects directly from command handlers.
- Do not swallow exceptions. Exception capture must never hide the original failure.
- Include actionable context such as symbol, path, command, and expected state.
- Keep diagnostics and progress on stderr and primary results on stdout.

### Command, descriptor, and workspace patterns

- New public commands implement `IRtCommand` and exactly one category marker.
- Define canonical names, examples, supported formats, and `WhenToUse` text in `RtCommandInfo`.
- `WhenToUse` should name the triggering situation, not merely restate capability. When replacing a built-in agent action such as Read or Edit, say so explicitly.
- Reuse shared option catalogues and binders rather than creating duplicate known flags.
- Prefer a typed application port when exposing the same operation through CLI and MCP.
- Do not add a bespoke MCP handler when the generic descriptor adapter can expose the port.
- Register MSBuild before using MSBuild workspace types.
- Dispose workspace sessions correctly.
- Re-resolve symbols against updated `Solution` snapshots after syntax-changing edits.
- Treat `find-unused` conservatively around reflection, framework activation, interface dispatch, and overrides.

### Editing expectations for agents

- Make minimal, task-scoped changes.
- Preserve the core-service and thin-host-adapter architecture.
- Avoid broad refactors unless requested or required to preserve an enforced contract.
- Avoid new dependencies unless they solve a demonstrated need.
- Update an ADR and architecture overview when changing a layer contract, output doctrine, enforced invariant, or accepted non-obvious trade-off.
- Do not hand-edit `docs/COMMANDS.md`; run `npm run generate:commands` to generate it from `rt dump-help`.
- Call out unavoidable formatting-only edits in the final report.

## Completion discipline

- Continue until the requested outcome is genuinely complete or a concrete blocker requires user authority.
- Do not describe partial progress, a plan, or focused test results as completion.
- Before finishing, verify applicable acceptance criteria, tests, formatting, coverage, mutation gates, ticket status, commits, pushes, and repository synchronization.
- Commit and push each completed atomic slice promptly.
- Every commit must have a concise subject and a detailed body explaining why, what changed, verification, constraints, and follow-up.
- Update linked tickets after every commit.
- If blocked, report the exact blocker, evidence gathered, remaining work, and decision required.

### Worktree discipline

- Run `git worktree list` and inspect every worktree's branch and status before and after the task.
- Never reset, overwrite, clean, or remove a dirty worktree containing unrelated work.
- Finish, integrate, push, or explicitly hand over active worktree changes.
- Remove temporary worktrees only after they are clean and their commits are integrated and pushed.
- Confirm the primary branch is clean and synchronized before declaring completion.

## Verification checklist

For most changes:

```powershell
dotnet format "RoslynTools.slnx" --verify-no-changes
dotnet build "RoslynTools.slnx" -c Release
pwsh -NoProfile -File "scripts/check-local.ps1" -Full -Lane all -Configuration Release
```

For a narrow change, run the smallest meaningful test first. Before handoff, use the local verification gate unless the task explicitly requires a different workflow.

Read and follow the rules in [rule-sets/tool-repos.md](rule-sets/tool-repos.md).
