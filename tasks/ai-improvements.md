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
| 14 | Fix `extract-method --replace-duplicates` assignment capture | Accuracy | High | When `--replace-duplicates` finds a duplicate that is an assignment (`var x = <expr>`), the replacement emits a bare void call instead of `var x = MethodName(...)`, leaving the variable undefined. Root cause: `DuplicateReplacer` does not check whether the original statement node is a `LocalDeclarationStatementSyntax` before emitting the call. Fix: detect the assignment case and emit `var <variableName> = <call>` in the replacement. Logged in dogfood-gaps.md 2026-04-19 (tour-store otpauthUri extraction). |
| 13 | ~~Fix workspace load crash when invoked cross-process from bash~~ **FIXED 2026-04-18** | Accuracy | ~~**Critical**~~ | Root cause was single-file publish, not the bash/cmd invocation route. Roslyn's `BuildHostProcessManager.CreateDotNetCoreBuildHostStartInfo` uses `typeof(BuildHostProcessManager).Assembly.Location` to locate the `BuildHost-netcore/` folder; under `PublishSingleFile=true`, `Assembly.Location` is empty for bundled managed assemblies, so `Path.GetDirectoryName("")` returns null and `Path.Combine(null, ...)` throws. Fix: added `<IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>` to the publish `PropertyGroup` in `src/RoslynTools/RoslynTools.csproj`. The runtime now extracts all bundled content to a temp directory on first launch, restoring a meaningful `Assembly.Location`. Verified via `cmd //c rt.cmd inspect`, `cmd //c rt.cmd list-smells`. `dotnet run` was never affected because that path uses the normal (non-bundled) build output. |

---

## Related files

- `C:\code\roslyn-tools\tasks\dogfood-gaps.md` — runtime gaps logged during daily use
- `C:\code\roslyn-tools\tasks\prd-refactoring-commands.md` — PRD for US-001 through US-008 (new commands)

## 2026-04-20 additions

- Better default solution discovery and repo defaults: omitted `--solution` is still too fragile for daily use in larger repos. `WorkspaceLoader` should walk upward from `cwd`, choose the nearest `.sln`, optionally honor a repo-level default, and emit a concrete recovery message when multiple candidates exist.
- Separate symbol results from workspace diagnostics: read-only exploration commands should not blur semantic results with workspace warnings. Output should distinguish symbol results, non-fatal workspace diagnostics, and hard-failure conditions so package audit warnings and transient file-lock issues do not look like hard blockers when Roslyn still produced usable semantic data.
- `analyze-interface` consumer/member usage report: add a command such as `rt analyze-interface IStorageService` that groups consumers and lists the exact interface members each one calls. The core question for interface segregation is "which methods does this type actually need?" and raw `find-refs` output does not answer that directly.
- Minimal replacement interface suggestions per consumer: extend analysis so the tool can propose a narrower abstraction per consumer or list the exact subset of members needed by a type such as `PublicTwoFactorController`. This is the missing bridge between reference discovery and safe dependency narrowing.
- Narrow-dependency refactor workflow: add an assisted refactor that extracts selected interface members into a new interface, updates constructor parameter types and backing fields across consumers where the narrower type is sufficient, and optionally adds DI forwarding registrations. That turns interface segregation from manual reasoning plus edits into an actual tool-driven mutation.
- `rt add-test --cli <CommandClass>`: scaffold CLI-invocation tests that run the built `Command` through `InvokeAsync` and assert on stdout/stderr/exit-code. Required to satisfy the external-action testing rule established by the mutation pilot — current `rt add-test` only emits Core-level unit tests, so every command's CLI layer (Info metadata, Build() wiring, option registration, handler attachment) is untested. Scaffold should cover: required-arg-missing produces non-zero exit + error message, `--help` output contains expected options, dry-run invocation produces expected diff shape. Output a test class that uses `Verify.Xunit` for stdout snapshotting where appropriate.
- `rt dump-help --json --normalize`: stable, deterministic JSON output from `dump-help` so it can be snapshot-tested across environments. Strip or redact version strings, absolute paths, and any timestamps. Enables a single repo-wide snapshot test that kills all help-text string mutants across every command in one shot (observed on MakeSealedCommand pilot: 7 of 20 survivors were help-text strings).
- Audit `RtCommandInfo` fields for external-observer coverage: by the external-action rule, any field on `Info` that isn't surfaced through `rt --help`, `rt <cmd> --help`, or `rt dump-help --json` is cruft — mutations on it change nothing a user can observe. Trace every `Info` field (`Name`, `Mutates`, `JsonOutput`, `Category`, `Aliases`, `WhenToUse`, `Examples`, `KnownIssues`) through to external output; expose the ones that are actually referenced elsewhere in the code, delete the ones that aren't.
- **Standalone: Stryker.NET mutation testing**: pilot configured in `stryker-config.json` and `.config/dotnet-tools.json`, scoped to `Commands/MakeSealedCommand.cs`. Baseline 22.22% score with 20 survivors — all classified in `tasks/mutation-pilot.md`. Follow-up stories: (a) `CommandMetadataTests` via `rt add-test --cli` scaffolding + `rt dump-help --json` snapshot, expected to drive `Commands/` toward 100%; (b) expand scope to all of `Commands/` once external-action test infrastructure exists; (c) CI gate once any pipeline exists.

## Triage

### Do next

- Better default solution discovery and repo defaults
  Reason: highest day-to-day friction reducer, low conceptual risk, and it improves every command that loads a workspace.
- Separate symbol results from workspace diagnostics
  Reason: directly improves trust in read-only exploration output and should be implementable without inventing a new command surface.
- `analyze-interface` consumer/member usage report
  Reason: this is the highest-value missing analysis capability from the tour-store interface-segregation task and the smallest credible bridge between `find-refs` and automated narrowing.

### Fold into existing backlog

- Narrow-dependency refactor workflow
  Reason: belongs with the existing refactoring-command family in `tasks/prd-refactoring-commands.md`, not as a standalone AI-improvement item. It is a mutation workflow with enough surface area to deserve its own PRD slice.
- Minimal replacement interface suggestions per consumer
  Reason: should be scoped as phase 2 of `analyze-interface`, not a separate top-level story. First ship exact member-usage grouping, then add suggestion heuristics on top.

### Already covered elsewhere

- General `replace-call` command
  Reason: already tracked as item 4 in this file and linked to `rt-001`.
- `inject-dependency` with generic type + partial class support
  Reason: already tracked as item 5 in this file and is the nearest existing foundation for DI updates in any future narrow-dependency workflow.
- `dependency-graph` body-level symbol walk
  Reason: already tracked as item 12 in this file.
- `extract-method --replace-duplicates` assignment capture
  Reason: already tracked as item 14 in this file.

### Defer

- Repo-wide default solution config file
  Reason: useful, but should come after upward nearest-solution auto-detection. Auto-detect likely solves most of the pain with less new configuration surface.
- Full minimal-interface synthesis and automatic interface naming
  Reason: high heuristic risk. Ship exact per-consumer member usage first, then evaluate whether naming and interface-shape proposals are trustworthy enough to automate.
