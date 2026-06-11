@AGENTS.md

# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

A CLI refactoring tool (`rt`) that uses Roslyn to perform solution-wide C# refactoring operations (rename, move, find-refs) against the ProjectX solutions at `C:\code\`. Built on .NET 8, System.CommandLine, and Microsoft.CodeAnalysis.

## Architecture pattern: Core service + thin host adapters

Every new feature follows the **Core service + thin host adapters** pattern. Logic lives in a pure service in `src/RoslynTools/Services/` (no `System.CommandLine`, no `Console`, no IO except via injected dependencies). The CLI and MCP each have a *thin adapter*: the CLI command parses args + renders text/JSON; the MCP handler parses JSON + returns structured JSON. Both resolve the same service from DI.

Worked examples:

- **`suggest` (greenfield)** — `SuggestService` in `src/RoslynTools/Services/Suggest/`. `SuggestCommand` in `src/RoslynTools.Cli/Suggest/` (meta-command, registered at root next to `dump-help`). `SuggestHandler` in `src/RoslynTools.Mcp/Handlers/`.
- **`outline` (existing migration)** — Pre-existing `OutlineService` in `src/RoslynTools/Services/`. Added `OutlineRequest` DTO and `OutlineHandler` so MCP returns native JSON instead of text-captured CLI output.

Full write-up: [docs/pattern-core-service.md](docs/pattern-core-service.md). When adding new commands or migrating existing ones, follow that pattern. When in doubt about whether a piece of code belongs in the service or in a host adapter, ask: "could I unit-test this without any host references in scope?" If no, the code is in the wrong layer.

## Discovering commands

Don't know which command to reach for? `rt suggest "<task in plain English>"` ranks the catalog against a natural-language query and prints the top candidates with examples. Also wired into the unknown-command fallback: `rt rm-method` → suggestions for `inline-method`, `safe-delete`, etc.

## Eat Your Own Dog Food

When working on any C# refactoring task across the ProjectX codebase (or any codebase), ask: **could `rt` do this?** If you're about to manually rename a symbol, move a namespace, find references, or restructure a class — try the `rt` command first. If `rt` can't do it, or does it badly, that's a feature request or bug report for this project. Log it in `tasks/` or fix it.

This applies to work in *any* repo under `C:\code\`. The whole point of this tool is to replace manual refactoring.

## Build and Run

```powershell
dotnet build "RoslynTools.slnx"                                          # build
dotnet build "RoslynTools.slnx" -c Release                               # release build
dotnet run --project "src/RoslynTools.Cli/RoslynTools.Cli.csproj" -- --help # run with args
dotnet format "RoslynTools.slnx" --verify-no-changes                     # check formatting
dotnet format "RoslynTools.slnx"                                         # fix formatting
```

Wrapper scripts `rt.cmd` / `rt.ps1` / `rt.sh` auto-detect source changes and rebuild before running. No manual publish step needed — just run `rt` and it rebuilds if any `*.cs`, `*.csproj`, `*.slnx`, or `Directory.*.props` file under `src/` is newer than the binary.

```powershell
dotnet test "RoslynTools.slnx" -v minimal
dotnet test "tests/RoslynTools.Tests/RoslynTools.Tests.csproj" -v minimal --filter "FullyQualifiedName~TypeName"
```

## Test Organization and Naming

@C:\code\ai-rules\test-naming.md

Tests are organized by `rt` command category (Inspect/Analyze/Refactor), not by issue/ticket. See the guide above for naming conventions (`with_<scenario>_<outcome>`), trait grouping by concern, and test discovery patterns.

## Verification Checklist

After any code change, run both:
```powershell
dotnet build "RoslynTools.slnx"
dotnet format "RoslynTools.slnx" --verify-no-changes
```

## Mutation Testing

Stryker.NET is pinned via `.config/dotnet-tools.json` and configured in `stryker-config.json`. Currently piloted against `MakeSealedCommand` only; rollout to the rest of `Commands/` is tracked in the backlog.

```powershell
dotnet tool restore      # one-time, installs dotnet-stryker 4.14.1
dotnet stryker           # runs against the config'd file(s), reports to StrykerOutput/
```

Commit-time mutation gates are versioned under `scripts/git-hooks/`. Install them once per clone:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-git-hooks.ps1
```

The pre-commit gate fails staged production C# files that are not covered by `stryker-config.json` `mutate` globs. For files already in mutation scope, it runs Stryker with `--break-at 95`. Use `RT_MUTATION_ALLOW_UNSCOPED=1` only for an intentional temporary bypass that should be explained in the commit or PR.

**Target: 100% mutation score on `src/RoslynTools/Commands/`.** Every surviving mutant is a signal — classify it:

- **Missing test case** — real gap. The mutated code has observable behaviour but no test asserts on it. Add the assertion that would have killed the mutant.
- **Extra cruft** — the mutated code has no observable behaviour because it shouldn't exist. The test suite passing after the mutation is proof the code is dead / unneeded. Delete the code; do not add a test to justify it.
- **Equivalent mutant** — rare. Genuinely behaviour-preserving transformation (e.g. `>=` vs `>` on an unreachable branch). Document in a one-line `// mutation: equivalent — <reason>` comment and add to `ignore-mutations` in `stryker-config.json`.

Never weaken an assertion to make a mutant "die" — that's score fraud. The valid moves are "tighten test", "delete code", or "justify as equivalent". Nothing else.

## Dogfooding

After modifying or adding any command, test it against a real solution before marking work complete. Use the roslyn-tools solution itself (`RoslynTools.slnx`) as the default target — it's small enough to load fast but has enough types/members to exercise the commands.

**Command groups.** Every command lives under one of three top-level groups (`rt <group> <command>`):

- `rt inspect <cmd>` — read-only, point at a single symbol or file (`describe`, `outline`, `find-refs`, `find-callers`, `find-impl`, `find-unused`, `find-pattern`)
- `rt analyze <cmd>` — read-only, broader codebase scans (`circular-deps`, `dependency-graph`, `analyze-interface`, `find-duplicates`, `find-magic-strings`, `list-imports`, `list-smells`)
- `rt refactor <cmd>` — mutations (`rename`, `move`, `extract-*`, `inline-method`, `make-*`, `replace-*`, `convert-*`, `add-*`, etc.)

The unqualified form `rt <cmd>` is also accepted for every command (no name collides with a group). Examples below use the canonical nested form.

**Smoke test matrix** — run each applicable command after changes:
```powershell
# Read-only commands (safe, run freely)
rt inspect describe "RoslynTools.Core.WorkspaceLoader" --solution RoslynTools.slnx
rt inspect outline src/RoslynTools/Core/SymbolLocator.cs --solution RoslynTools.slnx
rt inspect find-refs --symbol WorkspaceLoader --solution RoslynTools.slnx
rt inspect find-impl "System.CommandLine.Command" --solution RoslynTools.slnx

# Mutation commands (use --dry-run only against self)
rt refactor rename "RoslynTools.Models.SymbolMatch.DisplayName" NewName --solution RoslynTools.slnx --dry-run
rt refactor move --type RoslynTools.Models.SymbolMatch --to-namespace RoslynTools.Core.Models --solution RoslynTools.slnx --dry-run
rt refactor extract-method src/RoslynTools/Core/SolutionWriter.cs PrintDiffBlock --start-line 72 --end-line 98 --solution RoslynTools.slnx --dry-run
```

**What to check:**
- Command exits 0 (no crash)
- `--dry-run` output looks correct (diff makes sense, no garbled text)
- stderr shows progress messages, stdout shows results
- No workspace load warnings that indicate missing projects

**For ProjectX testing** (larger, slower — use when relevant):
```powershell
rt inspect describe "ProjectX.Service.Services.ServiceController" --solution "C:\code\libraries\ProjectX.Libraries.sln"
rt inspect find-refs --symbol BookingsService --solution "C:\code\libraries\ProjectX.Libraries.sln"
```

## Commands

**The authoritative, always-current command reference is the app itself.** Run:
```powershell
rt dump-help
# or without publishing:
dotnet run --project src/RoslynTools.Cli/RoslynTools.Cli.csproj -- dump-help
```
The build also writes this output to `docs/COMMANDS.md` locally (gitignored) as a convenience — do not commit it. For an agent-friendly pre-loaded reference, load the `use-rt` skill.

`rt <cmd> --help` (and `rt <group> <cmd> --help`) now renders the same rich content `dump-help` does for that single command — description, when-to-use, examples, supported output formats, and known-issues. No need to scrape `dump-help` output to find one command's invocation.

## Command metadata contract (read before changing any command)

Every class in `src/RoslynTools/Commands/` ending with `Command` MUST implement `IRtCommand` (defined in `Core/IRtCommand.cs`). The compiler is strict enough that the registry is reflection-discovered — no hand-maintained list — and a static assertion in `App.cs` throws at startup (and therefore fails the post-build MD generation) if any class drifts from the contract.

When you:

- **Add a command** — create `FooCommand.cs` as `internal sealed class FooCommand : IRtCommand`, supply static `Info` + `Build()`, and you're done. `Info.Category` MUST be one of `"inspect"`, `"analyze"`, or `"refactor"` — `App.RunAsync` throws at startup otherwise. The registry and `docs/COMMANDS.md` pick it up automatically on next build.
- **Rename a command** — update `Info.Name` to match the `new Command("new-name", ...)` first arg.
- **Add / remove / rename a flag** — update `Info.Examples` so it reflects real invocations.
- **Change mutation or output semantics** — update `Info.Mutates` (writes files? true) and `Info.JsonOutput` (stdout is JSON? true) accordingly.
- **Discover a bug / gap** — log a backlog story via `/task` (writes to `ralph/backlog-prd.json`; priority reflects severity) and optionally add a line to the affected command's `Info.KnownIssues`.

After any change:

1. `dotnet build src/RoslynTools.Cli/RoslynTools.Cli.csproj` — this regenerates the local `docs/COMMANDS.md`.
2. Run `rt dump-help` — verify the output matches your intent.
3. Commit the source change only (`docs/COMMANDS.md` is gitignored).

## Architecture

```
Program.Main
  -> [fast path if args[0] == "dump-help"] -> App.RunAsync(args)   # no MSBuild needed
  -> MsBuildBootstrap.Register()     # locate & register MSBuild SDK
  -> App.RunAsync(args)              # build System.CommandLine root, dispatch

Commands/                            # CLI commands — each class implements IRtCommand
                                     # Full list: rt dump-help
  DumpHelpCommand                    # hidden meta-command; emits docs/COMMANDS.md content

Core/
  IRtCommand                         # compile-time contract: static RtCommandInfo Info + Command Build()
  RtCommandInfo                      # metadata record used by DumpHelpCommand
  WorkspaceLoader                    # open .sln/.slnx via MSBuildWorkspace
  SymbolLocator                      # find symbols by name/FQN; interactive prompt or --decisions YAML
  SolutionWriter                     # apply changes or print unified diff (dry-run)
  MethodExtractor                    # extract-method core logic (dataflow, call site, method builder)
  Decisions/                         # AI-driven disambiguation: DecisionEntry/Store/Context + exit code 2

MsBuildBootstrap                     # locate & register MSBuild SDK (top-level, runs before App)

Models/
  SymbolMatch                        # Symbol + DisplayName + ProjectName
```

**Data flow:** CLI parses args -> WorkspaceLoader opens solution -> SymbolLocator finds target symbol -> Roslyn APIs compute changes -> SolutionWriter writes files or shows diff.

**Key constraint:** `MSBuildLocator.Register()` must run before any Roslyn workspace type is loaded. This is why `MsBuildBootstrap` runs first in `Program.Main`, before `App` (which references MSBuildWorkspace types).

**MoveCommand has two modes:**
- Single-type (`--type`): uses Roslyn Renamer for the namespace change
- Batch (`--namespace`): uses a custom `CSharpSyntaxRewriter` to rewrite all namespace declarations and references

All commands support `--dry-run` for preview without file writes.

## Coding Style

See `AGENTS.md` for detailed style guidelines. Key points:
- File-scoped namespaces, 4-space indentation, Allman braces
- CLI option variables end with `Opt` (e.g., `solutionOpt`, `dryRunOpt`)
- Async all the way; no `.Result` / `.Wait()`
- Diagnostics to stderr, results to stdout
- C# 11+ features OK (nullable enabled, implicit usings)

## Result types: `Result<T, RtError>` vs throwing

Core services use a typed `Result<T, RtError>` ADT (`Core/Result.cs`, `Core/RtError.cs`,
`Core/RtErrorKind.cs`) to **return** routine, expected user-facing failures instead of
throwing them. Reach for `Result<T, RtError>` when the failure is a normal flow the user
will hit — symbol not found, ambiguous match, invalid input. **Throw** for invariant
violations and genuinely unexpected failures: those should surface with a stack trace and
get captured to `feedback.json` for triage. Rule of thumb: if you'd write a friendly
one-line message and exit 1, it's an `RtError`; if hitting it means a bug, throw.

- Consume a `Result` with `.Match(onOk, onErr)` (both branches total) or `.Map(...)`.
- CLI commands surface the error branch through the single shared helper
  `RtErrorPresenter.Surface(ctx, error)` (stderr `Error: <message>` + exit 1) — no
  per-command if/else ladder. MCP handlers map it to `new ToolResult(isError: true, message)`.
- `Result<T, RtError>` is **not** `RtResult<T>` (`Core/RtResult.cs`): the latter is the
  output envelope (status + diagnostics + exit code) for renderers. The two coexist by design.
- `.Unwrap()` is a temporary bridge: it re-throws the `RtError.Message` as
  `InvalidOperationException` for callers not yet migrated to consume the `Result`. A migration
  adds a Result-returning `Try*` variant and reimplements the old throwing method as a one-line
  `Try*().Unwrap()` bridge (**parallel-change**) so the signature flip stays incremental — one
  caller/tool at a time — with zero blast radius. Delete the bridge when the last caller moves.

### Per-tool target shape (migration in progress)

The codebase is being brought to a consistent **per-command target shape**, one tool at a
time: logic in a Core service · routine errors as `Result<T, RtError>` · output through the
shared renderer · thin CLI **and** native MCP adapters that both `Match` the same service
Result · complete `RtCommandInfo` metadata. `rt inspect describe` is the reference pilot
(`InspectService.Describe` + `DescribeCommand` + `Handlers/DescribeHandler`). Note: the typed
`IRtOutput` DTO facet is **not** universal — for commands with polymorphic output whose nested
shapes come from shared helpers (`SymbolFormatter`), a typed DTO is a separate EP-009 concern,
not a prerequisite. Don't retrofit a DTO just to "complete the shape" if it forces touching
shared code.

## Design Philosophy: Pragmatic Refactoring Over Architectural Purity

**Core principle:** Consolidate duplicates only when they genuinely solve real problems. Do not apply design patterns for their own sake.

**Pattern-driven consolidation:**
- Patterns (Factory, Builder, Strategy, etc.) are a **lens** for recognizing refactoring opportunities, not mandates
- When duplicates emerge (detected via `rt analyze find-duplicates`), ask: "Does this duplication cause real maintenance pain?"
- Apply a pattern **if and only if** it simplifies the code, not because Gang of Four says it's elegant
- Example: Multiple commands creating `new Option<string>(name, help) { IsRequired = true }` → **Factory Method** naturally emerges as the solution. Create `CommandOptionFactory.CreateStringOption()` because it eliminates real duplication, not for theoretical purity.

**Workflow for duplication discovery:**
1. Run `rt analyze find-duplicates --solution RoslynTools.slnx` to identify candidates
2. Examine high-scoring groups (>4 occurrences, score >5)
3. Ask: Is this duplication causing problems? (maintenance burden, inconsistency, bug vectors?)
4. If yes, look for the minimal consolidation pattern that solves it
5. Implement the pattern only if it's simpler than the alternative

**Anti-pattern:** Do not:
- Apply Builder pattern to command construction just because the Go4 book suggests it
- Introduce Strategy pattern because there are "multiple algorithms" (there are in any large system)
- Create abstract factories or complex hierarchies to avoid writing similar code twice
- Add indirection layers for code that appears in only 2–3 places

**Trust the duplication detector, not theories.** If `find-duplicates` reports a group with 8+ occurrences, that's a signal. If it reports 2 occurrences, wait for 3–5 before consolidating.

## Known Issues

All issues from the April 2026 audit have been resolved.

1. **PrintDiff is not LCS-based** (`SolutionWriter.cs`) — FIXED: replaced with DiffPlex Myers diff (US-001–004).
2. **`--solution` inconsistency**: `FindRefsCommand` and `MoveCommand` require `--solution`; all other commands auto-detect — FIXED (US-012, US-013).
3. **SolutionWriter can't create new files**: `GetAddedDocuments()` prints WARNING and skips — FIXED: `ApplyChangesAsync` now handles added documents (US-005–006).
4. **MethodExtractor missing input validation**: No `startLine <= endLine` or `>= 1` check — FIXED: `ArgumentOutOfRangeException` guards added (US-014).
5. **MsBuildBootstrap silent catch**: Empty catch for `DllNotFoundException` — FIXED: now logs to stderr (US-015).
6. **Duplication**: `JsonSerializerOptions`, location-to-dictionary mapping, and type outline building copy-pasted across commands — FIXED: `SymbolFormatter` helpers extracted (US-007–011).

## Output format convention

- **Read-only inspection commands**: JSON to stdout
- **Mutation commands**: plain text to stdout
- **All commands**: diagnostics/progress to stderr

## Exit codes

- `0` — success
- `1` — error
- `2` — decisions required (see `--decisions` below)

## --decisions \<path>

Global flag. When a command would prompt for symbol disambiguation (multiple matches for a name), passing `--decisions <path.yaml>` makes the prompt machine-driven instead:

1. First run with empty/missing file: `rt` writes pending entries to YAML and exits **2**. No mutations.
2. Fill `choose:` with the option index in the YAML.
3. Re-run the same command: `rt` reads answers, runs without prompting.

YAML schema is one entry per pending decision: `{ id, q, options: { 0: "...", 1: "..." }, choose: <int> }`. Schema lives at `src/RoslynTools/Core/Decisions/`.

## Dependencies

| Package | Purpose |
|---|---|
| `System.CommandLine` (2.0.0-beta4) | CLI parsing |
| `Microsoft.Build.Locator` (1.7.8) | Find MSBuild installation |
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` (4.9.2) | Load solutions |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` (4.9.2) | C# syntax/semantic analysis |
| `DiffPlex` (1.7.1) | Unified diff output for --dry-run |
| `YamlDotNet` (16.2.1) | Read/write `--decisions` files |

