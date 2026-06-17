# AGENTS.md
Guidance for agentic coding tools working in `C:\code\roslyn-tools`.

## Project skills

@C:\code\roslyn-tools\.agents\skills\session-log\SKILL.md

---

## Project Overview
- Stack: C# on .NET 8 (`net8.0`).
- Solution: `RoslynTools.slnx`.
- Main CLI project: `src/RoslynTools.Cli/RoslynTools.Cli.csproj`.
- Core library project: `src/RoslynTools/RoslynTools.csproj`.
- App type: CLI (`OutputType=Exe`) using `System.CommandLine`.
- Nullable reference types: enabled.
- Implicit usings: enabled.
- Layout:
  - `src/RoslynTools/Commands`: CLI command builders/handlers. Each `*Command.cs` implements `IRtCommand`.
  - `src/RoslynTools/Core`: Roslyn workspace/symbol/change logic + the `IRtCommand` contract and `RtCommandInfo` metadata record.
  - `src/RoslynTools/Models`: small model types.

## Command reference
**Run `rt dump-help` to view the full command reference** (or `dotnet run --project src/RoslynTools.Cli/RoslynTools.Cli.csproj -- dump-help` without publishing). For an agent-friendly pre-loaded reference, load the `use-rt` skill. See `CLAUDE.md` → "Command metadata contract" for the workflow when adding or changing a command.

## Cursor and Copilot Rules
No repository-specific rule files were found:
- `.cursorrules`: not present
- `.cursor/rules/`: not present
- `.github/copilot-instructions.md`: not present
If these appear later, treat them as higher-priority guidance and merge with this file.

## Setup
Run from repository root:
```powershell
dotnet --version
dotnet restore "RoslynTools.slnx"
```
Use PowerShell syntax by default.

## Agent-Friendly CLI Conventions
- Prefer non-interactive commands and flags suitable for automation.
- Use deterministic output when possible (for example `-v minimal`).
- Quote file paths in commands to avoid shell parsing issues.
- Avoid prompts in scripts/commands that require manual input.
- Prefer exit-code driven checks so agents can reliably detect failures.
- Keep command output concise and parseable; avoid noisy one-off wrappers.
- **Parallel-safe by default.** `rt` is hardened for concurrent invocation from parallel agent worktrees: `rt feedback add`, wrapper rebuild, `deploy-skill`, and (with `RT_ISOLATED_OBJ=1`) MSBuild obj/ are all race-free. See CLAUDE.md → "Parallel safety" for details.

## Build Commands
Build entire solution:
```powershell
dotnet build "RoslynTools.slnx"
```
Build Release:
```powershell
dotnet build "RoslynTools.slnx" -c Release
```
Build only CLI project:
```powershell
dotnet build "src/RoslynTools.Cli/RoslynTools.Cli.csproj"
```
Run CLI help locally:
```powershell
dotnet run --project "src/RoslynTools.Cli/RoslynTools.Cli.csproj" -- --help
```

## Lint and Formatting
No dedicated lint script exists; use `dotnet format`.
Verify formatting/analyzers without making edits:
```powershell
dotnet format "RoslynTools.slnx" --verify-no-changes
```
Apply formatting/analyzer fixes:
```powershell
dotnet format "RoslynTools.slnx"
```
Notes:
- Current baseline has existing whitespace issues.
- Keep formatting changes limited to touched files unless asked otherwise.

## Test Commands
Test project: `tests/RoslynTools.Tests/RoslynTools.Tests.csproj`

Run all tests in solution:
```powershell
dotnet test "RoslynTools.slnx" -v minimal
```
Run tests for one project:
```powershell
dotnet test "tests/RoslynTools.Tests/RoslynTools.Tests.csproj" -v minimal
```
Run a single test (recommended first step):
```powershell
dotnet test "tests/RoslynTools.Tests/RoslynTools.Tests.csproj" -v minimal --filter "FullyQualifiedName=Namespace.TypeName.TestMethod"
```
Run tests by partial name:
```powershell
dotnet test "tests/RoslynTools.Tests/RoslynTools.Tests.csproj" -v minimal --filter "FullyQualifiedName~TypeName"
```
Useful filter keys (framework-dependent):
- `FullyQualifiedName`
- `Name`
- `ClassName`
- `Category` / `TestCategory`

## Coding Style Guidelines
Follow existing patterns in nearby files first.

### Imports and Namespaces
- Use file-scoped namespaces (`namespace RoslynTools.Core;`).
- Keep `using` directives at file top.
- Order usings as: `System.*`, then `Microsoft.*`, then `RoslynTools.*`.
- Remove unused usings.

### Formatting
- Use 4 spaces; no tabs.
- Use Allman braces (`{` on next line).
- Prefer one statement per line.
- Keep lines readable; wrap long argument lists.

### Types and Nullability
- Respect nullable context; annotate optional refs with `?`.
- Validate external inputs early; fail fast with clear exceptions.
- Prefer `var` when type is obvious from RHS.
- Use `required` init properties for required model fields when appropriate.

### Async Practices
- Prefer async all the way (`Task`/`Task<T>`).
- Avoid blocking calls (`.Result`, `.Wait()`).
- Keep handlers and entry points small and composable.

### Naming
- Types/methods/properties: PascalCase.
- Locals/parameters: camelCase.
- CLI option variables end with `Opt` (for example `solutionOpt`).
- Use descriptive Roslyn-oriented names (`solution`, `workspace`, `sym`).

### Error Handling and Output
- Throw specific exceptions (`FileNotFoundException`, `InvalidOperationException`).
- Include actionable context (symbol, file path, expected state).
- Do not swallow exceptions.
- For CLI UX: diagnostics/progress to stderr, primary results to stdout.

### Command and Workspace Patterns
- Define commands in `Commands/*Command.cs` via `System.CommandLine.Command`.
- Set option descriptions clearly; mark required options with `IsRequired`.
- Use typed `cmd.SetHandler(...)` signatures matching option declarations.
- Register `MSBuildLocator` before any MSBuild workspace type usage:
  `Program.Main` -> `MSBuildLocator.RegisterDefaults()` -> `App.RunAsync`.
- Dispose `MSBuildWorkspace` with `using`.
- Re-resolve symbols after syntax-changing edits against updated `Solution` snapshots.

### Editing Expectations for Agents
- Make minimal, task-scoped changes.
- Preserve current architecture (`Commands`, `Core`, `Models`).
- Avoid broad refactors unless requested.
- Avoid new dependencies unless required.
- Call out unavoidable formatting-only edits in your final report.

## Verification Checklist
For most code changes:
```powershell
dotnet build "RoslynTools.slnx"
dotnet format "RoslynTools.slnx" --verify-no-changes
```
If relevant tests exist, run the narrowest possible `dotnet test` filter first, then broaden.
