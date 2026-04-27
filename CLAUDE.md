# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

A CLI refactoring tool (`rt`) that uses Roslyn to perform solution-wide C# refactoring operations (rename, move, find-refs) against the ProjectX solutions at `C:\code\`. Built on .NET 8, System.CommandLine, and Microsoft.CodeAnalysis.

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

Wrapper scripts `rt.cmd` / `rt.ps1` run the built executable directly. **Always publish before using `rt.cmd`** — it runs the published binary, not the build output:

```powershell
dotnet publish src/RoslynTools.Cli/RoslynTools.Cli.csproj -c Release -r win-x64 --self-contained false
```

```powershell
dotnet test "RoslynTools.slnx" -v minimal
dotnet test "tests/RoslynTools.Tests/RoslynTools.Tests.csproj" -v minimal --filter "FullyQualifiedName~TypeName"
```

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

**Target: 100% mutation score on `src/RoslynTools/Commands/`.** Every surviving mutant is a signal — classify it:

- **Missing test case** — real gap. The mutated code has observable behaviour but no test asserts on it. Add the assertion that would have killed the mutant.
- **Extra cruft** — the mutated code has no observable behaviour because it shouldn't exist. The test suite passing after the mutation is proof the code is dead / unneeded. Delete the code; do not add a test to justify it.
- **Equivalent mutant** — rare. Genuinely behaviour-preserving transformation (e.g. `>=` vs `>` on an unreachable branch). Document in a one-line `// mutation: equivalent — <reason>` comment and add to `ignore-mutations` in `stryker-config.json`.

Never weaken an assertion to make a mutant "die" — that's score fraud. The valid moves are "tighten test", "delete code", or "justify as equivalent". Nothing else.

## Dogfooding

After modifying or adding any command, test it against a real solution before marking work complete. Use the roslyn-tools solution itself (`RoslynTools.slnx`) as the default target — it's small enough to load fast but has enough types/members to exercise the commands.

**Smoke test matrix** — run each applicable command after changes:
```powershell
# Read-only commands (safe, run freely)
rt inspect "RoslynTools.Core.WorkspaceLoader" --solution RoslynTools.slnx
rt outline src/RoslynTools/Core/SymbolLocator.cs --solution RoslynTools.slnx
rt find-refs --symbol WorkspaceLoader --solution RoslynTools.slnx
rt find-impl "System.CommandLine.Command" --solution RoslynTools.slnx

# Mutation commands (use --dry-run only against self)
rt rename "RoslynTools.Models.SymbolMatch.DisplayName" NewName --solution RoslynTools.slnx --dry-run
rt move --type RoslynTools.Models.SymbolMatch --to-namespace RoslynTools.Core.Models --solution RoslynTools.slnx --dry-run
rt extract-method src/RoslynTools/Core/SolutionWriter.cs PrintDiffBlock --start-line 72 --end-line 98 --solution RoslynTools.slnx --dry-run
```

**What to check:**
- Command exits 0 (no crash)
- `--dry-run` output looks correct (diff makes sense, no garbled text)
- stderr shows progress messages, stdout shows results
- No workspace load warnings that indicate missing projects

**For ProjectX testing** (larger, slower — use when relevant):
```powershell
rt inspect "ProjectX.Service.Services.ServiceController" --solution "C:\code\libraries\ProjectX.Libraries.sln"
rt find-refs --symbol BookingsService --solution "C:\code\libraries\ProjectX.Libraries.sln"
```

## Commands

**The authoritative, always-current command reference is the app itself.** Run:
```powershell
rt dump-help
# or without publishing:
dotnet run --project src/RoslynTools.Cli/RoslynTools.Cli.csproj -- dump-help
```
The build also writes this output to `docs/COMMANDS.md` locally (gitignored) as a convenience — do not commit it. For an agent-friendly pre-loaded reference, load the `use-rt` skill.

## Command metadata contract (read before changing any command)

Every class in `src/RoslynTools/Commands/` ending with `Command` MUST implement `IRtCommand` (defined in `Core/IRtCommand.cs`). The compiler is strict enough that the registry is reflection-discovered — no hand-maintained list — and a static assertion in `App.cs` throws at startup (and therefore fails the post-build MD generation) if any class drifts from the contract.

When you:

- **Add a command** — create `FooCommand.cs` as `internal sealed class FooCommand : IRtCommand`, supply static `Info` + `Build()`, and you're done. The registry and `docs/COMMANDS.md` pick it up automatically on next build.
- **Rename a command** — update `Info.Name` to match the `new Command("new-name", ...)` first arg.
- **Add / remove / rename a flag** — update `Info.Examples` so it reflects real invocations.
- **Change mutation or output semantics** — update `Info.Mutates` (writes files? true) and `Info.JsonOutput` (stdout is JSON? true) accordingly.
- **Discover a bug / gap** — log to `tasks/dogfood-gaps.md` and either raise a new row in `tasks/ai-improvements.md` or add a line to the affected command's `Info.KnownIssues`.

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
  SymbolLocator                      # find symbols by name/FQN, interactive disambiguation
  SolutionWriter                     # apply changes or print unified diff (dry-run)
  MethodExtractor                    # extract-method core logic (dataflow, call site, method builder)

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

## Dependencies

| Package | Purpose |
|---|---|
| `System.CommandLine` (2.0.0-beta4) | CLI parsing |
| `Microsoft.Build.Locator` (1.7.8) | Find MSBuild installation |
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` (4.9.2) | Load solutions |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` (4.9.2) | C# syntax/semantic analysis |
| `DiffPlex` (1.7.1) | Unified diff output for --dry-run |
