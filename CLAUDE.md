# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

A CLI refactoring tool (`rt`) that uses Roslyn to perform solution-wide C# refactoring operations (rename, move, find-refs) against the ProjectX solutions at `C:\code\`. Built on .NET 8, System.CommandLine, and Microsoft.CodeAnalysis.

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

## Architecture

```
Program.Main
  -> MsBuildBootstrap.Register()     # locate & register MSBuild SDK
  -> App.RunAsync(args)              # build System.CommandLine root, dispatch

Commands/                            # CLI command definitions (options, handler)
  RenameCommand     (alias: ren)     # rename symbol + all references
  MoveCommand                        # move type or entire namespace (single + batch)
  FindRefsCommand                    # find all references to a symbol

Core/                                # Roslyn workspace/symbol/change logic
  WorkspaceLoader                    # open .sln/.slnx via MSBuildWorkspace
  SymbolLocator                      # find symbols by name/FQN, interactive disambiguation
  SolutionWriter                     # apply changes or print unified diff (dry-run)

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

## Dependencies

| Package | Purpose |
|---|---|
| `System.CommandLine` (2.0.0-beta4) | CLI parsing |
| `Microsoft.Build.Locator` (1.7.8) | Find MSBuild installation |
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` (4.9.2) | Load solutions |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` (4.9.2) | C# syntax/semantic analysis |
