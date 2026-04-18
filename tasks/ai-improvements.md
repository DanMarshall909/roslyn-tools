# rt AI Improvements

Improvements to make `rt` more useful to AI agents — covering discoverability, output consistency, accuracy, and missing commands. Ordered by priority within category.

---

## Background

Audit findings from 2026-04-18:

- `--help` lists 26+ commands flat with no grouping; no machine-readable schema dump
- `SymbolLocator.Disambiguate` calls `Console.ReadLine()` — AI agents deadlock on multi-match
- Output format is inconsistent: `inspect`, `outline`, `find-impl`, `dependency-graph` emit JSON; all other commands emit plain text with no `--json` override
- Two confirmed `inline-method` bugs in dogfood-gaps.md
- `dependency-graph` misses local variable type usages inside method bodies
- No general `replace-call` command (only hardcoded `replace-datetime`)

---

## Improvements

| # | Title | Category | Priority | Change needed |
|---|---|---|---|---|
| 1 | `--no-interactive` / `--pick-first` flag on all commands | Usability | High | Add a global flag that prevents `SymbolLocator.Disambiguate` from calling `Console.ReadLine`. When multiple symbols match: if `--pick-first` is set, use the first result; if `--no-interactive` is set, exit 1 and emit a JSON array of candidates on stderr so the agent can re-invoke with a disambiguated symbol. Fixes agent deadlock on multi-match. |
| 2 | `list-commands` command with `--json` output | Discoverability | High | New read-only command that returns a JSON array of `{name, aliases, description, requiredOptions, mutates, dryRunSupported}` for every registered command. Allows an agent to enumerate capabilities without parsing `--help` text. |
| 3 | `--json` output flag on text-based commands | Usability | High | Add `--json` to `find-refs`, `find-callers`, `find-unused`, and `list-smells`. Each command already has a defined result shape; wrap it in a JSON array instead of the current fixed-width / `file(line): text` plain-text format. Agents can then consume results without fragile text parsing. |
| 4 | General `replace-call` command | New command | High | Already tracked in rt-001. Replaces any method invocation pattern across a solution (not just `DateTime.UtcNow`). Required for `Console.*` → `ILogger` migration (logged in dogfood-gaps.md). Must support `--dry-run`. |
| 5 | `inject-dependency` with generic type + partial class support | New command | High | Already tracked in rt-001. Generalises `ReplaceTypeCommand`'s constructor injector to handle `ILogger<T>` generics and partial class constructors. Current `add-dependency` story (US-005 in PRD) does not cover partial classes or open generics. |
| 6 | `--ignore-case` flag on symbol lookup | Accuracy | Medium | `FindDeclarationsAsync` is invoked with `ignoreCase: false`. Agents frequently don't know exact casing (e.g. `agentService` vs `AgentService`). Add `--ignore-case` flag that passes `ignoreCase: true` to the lookup. |
| 7 | Machine-readable mutation summary on stderr | Usability | Medium | After any mutation command (`rename`, `move`, `extract-interface`, `extract-class`, `inline-method`, etc.), emit a single JSON line to stderr: `{"command":"rename","filesModified":3,"symbolsUpdated":12}`. Agents can capture stderr to confirm what changed without parsing stdout diff text. |
| 8 | Fix `inline-method` method removal scope | Accuracy | Medium | Bug logged in dogfood-gaps.md (2026-04-17). Method removal after inlining matches by identifier text only — removes the wrong method when the same name exists in a different class in the same file. Fix: scope the removal to the containing `TypeDeclarationSyntax` matched by type name and parameter count. |
| 9 | Fix `inline-method` arg parenthesisation | Accuracy | Medium | Bug logged in dogfood-gaps.md (2026-04-17). `ParameterSubstitutor` does not parenthesise substituted arguments — `Double(a+b)` inlines as `a+b*2` instead of `(a+b)*2`. Fix: wrap non-trivial argument expressions in `ParenthesizedExpressionSyntax` before substitution. |
| 10 | `--exit-zero-on-empty` flag for read-only commands | Usability | Medium | Currently zero results and a genuine error both produce ambiguous exit codes. Add `--exit-zero-on-empty` so agents can use exit code to distinguish "ran successfully, nothing found" (0) from "error" (1) without parsing output text. Affects `find-refs`, `find-callers`, `find-unused`, `list-smells`. |
| 11 | Group commands in `--help` output | Discoverability | Low | Use System.CommandLine's help builder to render commands in labelled sections matching the README table (e.g. Navigation, Mutation, Analysis, Utilities). Reduces cognitive load when a developer or agent reads `rt --help` for the first time. |
| 12 | `dependency-graph` body-level symbol walk | Usability | Low | Gap logged in dogfood-gaps.md (2026-04-18). Graph edges are built from fields, method parameters/returns, base types, and interfaces — local variable type usages inside method bodies are missed. Static command classes with no fields appear as isolated nodes even when they use `WorkspaceLoader`/`SolutionWriter` internally. Fix: add `IdentifierNameSyntax` walk inside method bodies during edge collection. |
| 13 | Fix workspace load crash when invoked cross-process from bash | Accuracy | **Critical** | Bug logged in dogfood-gaps.md (2026-04-18). **Blocks dogfooding rt inside Claude Code parallel worker agents entirely** — every rt command that calls `WorkspaceLoader.LoadAsync` (which is all of them that touch code) throws `System.ArgumentNullException: Value cannot be null. (Parameter 'path1')` at `BuildHostProcessManager.CreateDotNetCoreBuildHostStartInfo` when rt is launched via `cmd //c` from a bash shell. Confirmed impact 2026-04-18: an /execute session ran ~10 controller-split/service-extraction stories (the canonical `rt move` / `rt extract-method` / `rt find-refs` use cases) and not one used rt, because workers can't invoke it. Fix: (a) call `MSBuildLocator.RegisterDefaults()` before `MSBuildWorkspace.Create()` in `WorkspaceLoader` — likely resolves the root cause; (b) as defence-in-depth, catch the NullRef in `WorkspaceLoader.LoadAsync` and emit a clear error (`rt requires DOTNET_ROOT or a Visual Studio MSBuild installation — set DOTNET_ROOT to the .NET SDK root`) with exit code 2. |

---

## Related files

- `C:\code\roslyn-tools\tasks\dogfood-gaps.md` — runtime gaps logged during daily use
- `C:\code\roslyn-tools\tasks\prd-refactoring-commands.md` — PRD for US-001 through US-008 (new commands)
