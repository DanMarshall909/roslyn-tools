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
dotnet run --project "src/RoslynTools/RoslynTools.csproj" -- --help       # run with args
dotnet format "RoslynTools.slnx" --verify-no-changes                     # check formatting
dotnet format "RoslynTools.slnx"                                         # fix formatting
```

Wrapper scripts `rt.cmd` / `rt.ps1` run the built executable directly.

No test project exists yet. When one is added:
```powershell
dotnet test "RoslynTools.slnx" -v minimal
dotnet test "path/to/Tests.csproj" -v minimal --filter "FullyQualifiedName~TypeName"
```

## Verification Checklist

After any code change, run both:
```powershell
dotnet build "RoslynTools.slnx"
dotnet format "RoslynTools.slnx" --verify-no-changes
```

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

## Architecture

```
Program.Main
  -> MsBuildBootstrap.Register()     # locate & register MSBuild SDK
  -> App.RunAsync(args)              # build System.CommandLine root, dispatch

Commands/                            # CLI command definitions (options, handler)
  RenameCommand          (alias: ren)    # rename symbol + all references
  MoveCommand                            # move type or entire namespace (single + batch)
  FindRefsCommand                        # find all references to a symbol
  InspectCommand                         # show type shape: members, bases, interfaces (JSON)
  OutlineCommand                         # list all types/members in a file or namespace (JSON)
  FindImplementationsCommand (alias: impls) # find all implementations of interface/abstract class (JSON)
  ReplaceTypeCommand                     # replace DateTime.UtcNow/Now with TimeProvider injection
  ExtractMethodCommand   (alias: em)     # extract statements into a new private method

Core/                                # Roslyn workspace/symbol/change logic
  WorkspaceLoader                    # open .sln/.slnx via MSBuildWorkspace
  SymbolLocator                      # find symbols by name/FQN, interactive disambiguation
  SolutionWriter                     # apply changes or print unified diff (dry-run)
  MethodExtractor                    # extract-method core logic (dataflow, call site, method builder)
  MsBuildBootstrap                   # locate & register MSBuild SDK (in root, not Core/)

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

These are verified issues from the April 2026 audit. Fix before adding new commands.

1. **PrintDiff is not LCS-based** (`SolutionWriter.cs`): Naive algorithm advances both pointers in lockstep — single-line insertions produce huge diffs. Replace with real diff or `git diff --no-index`.
2. **`--solution` inconsistency**: `FindRefsCommand` and `MoveCommand` require `--solution`; all other commands auto-detect. Make all optional.
3. **SolutionWriter can't create new files**: `GetAddedDocuments()` prints WARNING and skips. Must be upgraded before extract-interface/extract-class.
4. **MethodExtractor missing input validation**: No `startLine <= endLine` or `>= 1` check.
5. **MsBuildBootstrap silent catch**: Empty catch for `DllNotFoundException` — should log to stderr.
6. **Duplication**: `JsonSerializerOptions`, location-to-dictionary mapping, and type outline building are copy-pasted across InspectCommand, OutlineCommand, FindImplementationsCommand. Extract shared helpers before adding more commands.

## Output format convention

- **Read-only inspection commands** (inspect, outline, find-impl): JSON to stdout
- **Mutation commands** (rename, move, extract-method, replace-datetime): plain text to stdout
- **All commands**: diagnostics/progress to stderr

## Dependencies

| Package | Purpose |
|---|---|
| `System.CommandLine` (2.0.0-beta4) | CLI parsing |
| `Microsoft.Build.Locator` (1.7.8) | Find MSBuild installation |
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` (4.9.2) | Load solutions |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` (4.9.2) | C# syntax/semantic analysis |
