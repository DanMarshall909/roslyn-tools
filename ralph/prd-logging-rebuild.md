# PRD: Logging refactor on top of fixed main

**Branch:** `ralph/logging-rebuild`
**Targets:** `main`
**Working tree state:** 10 logging-infrastructure files cherry-picked from tag `pre-cli-services-rebase` are uncommitted in the working tree (will be the deliverable of US-002).

## Problem

`origin/main` (HEAD `19d5dfb`) is broken: PR #1 ("Refactor: Extract command logic into service layer") merged with 5 build errors. The library project `RoslynTools` references result types (`FindCallersResult`, `FindRefsResult`) that PR #1 moved to `RoslynTools.Cli`, creating a circular project ref.

Separately, the in-flight logging refactor on the old `ralph/logging` branch (preserved as tag `pre-cli-services-rebase`) cannot rebase onto the new main shape — every command file it touched moved to the Cli project. A prior Opus diff review surfaced 3 BLOCKERs in the original migration that need addressing this time around.

We are doing both pieces of work as a single coordinated effort: fix main, then land the logging refactor on top using a Haiku swarm via `/execute`. The swarm makes the 139-call-site migration tractable in parallel; the BLOCKER awareness baked into worker contracts prevents reintroducing the regressions.

## Goals

1. **`origin/main` builds green** with zero compile errors and zero warnings.
2. **All `Console.*` direct references outside `RtConsole.cs` are eliminated** in `src/RoslynTools/`, `src/RoslynTools.Cli/`, and the test project. Architecture gate (`ConsoleGateTests`) enforces this.
3. **Channel correctness** matches CLAUDE.md "Output format convention":
   - Read-only inspection commands → JSON to stdout.
   - Mutation commands → plain-text result to stdout (including no-op result messages like "is already sealed").
   - Diagnostics / progress / errors → stderr.
   - Dry-run unified diffs → stdout (Diff context).
4. **Structured logging templates** (`{Field}` placeholders, not `$"…"` interpolation) used everywhere log calls are made. Enforced by `StructuredLoggingTests`.
5. **Three BLOCKERs from the prior review are resolved:**
   - **B1** `WorkspaceLoader` marker lines (`---`, `[workspace-warnings]`) emit raw on stderr without a `[WARNING] ` prefix.
   - **B2** All 12 mutation-command no-op result messages route to stdout via `RtConsole.WriteLine(RtOutputContext.Result, …)`, never to `RtLog.Info` (which goes to stderr).
   - **B3** `FindDuplicatesResult.Link` shape stays `path:start-end` (currently correct on main; just don't regress).

## Non-goals

- **Other circular-ref result types** that don't currently break the build (`AnalyzeInterfaceResult`, `FindDuplicatesResult`, `FindUnusedResult`, `CircularDepsResult`, `ListSmellsResult`). Track in `docs/issues/` and move opportunistically if a story already touches the file. Not in scope for this PRD.
- **Adding required CI on main.** PR #1 merged broken because GitHub had no required build check. Out of scope here; track separately.
- **Replacing `Console.*` in test fixtures or `AdhocProjectBuilder`** that intentionally write to host stdout for test diagnostics. The architecture gate should allow these explicitly.
- **Color-encoding refactor.** Existing `ConsoleColor` arguments fold into the new `RtConsole.WriteLine(context, msg, color)` signature. No new color abstraction.

## User stories

### US-001 — Fix main: result types in library

Move `FindCallersResult.cs` and `FindRefsResult.cs` from `src/RoslynTools.Cli/Commands/Results/` to `src/RoslynTools/Services/Results/` (namespace `RoslynTools.Services.Results`). Update consumers' `using` directives. Fix `src/RoslynTools/Core/AnalyzeInterfaceCore.cs:3,262` to call `FindCallersService.IsNameofExpression()` directly (drop `using RoslynTools.Commands;`). Goal: `dotnet build "RoslynTools.slnx"` exits 0 with 0 warnings, all existing tests still pass.

**Branch**: `fix/services-result-types` off `origin/main`. Lands as its own PR before any logging work.

### US-002 — Logging infrastructure

Add the 6 new `Core/` infrastructure files (`IRtConsole.cs`, `IRtLogger.cs`, `RtConsole.cs`, `RtLog.cs`, `RtLogLevel.cs`, `RtOutputContext.cs`), `docs/logging-standards.md`, and 3 test files (`ConsoleGateTests.cs`, `StructuredLoggingTests.cs`, `LoggingRefactorTests.cs`) under `tests/RoslynTools.Tests/Infrastructure/`. The architecture gate `ConsoleGateTests` is initially marked `[Trait("Migration", "DeferEnforcement")]` and skipped — enabled in US-009 once migration completes. The other two tests (`StructuredLoggingTests`, `LoggingRefactorTests`) are green and enforced from US-002 onward. `LoggingRefactorTests` may need import fixes for commands renamed by PR #1.

Files cherry-pick cleanly from tag `pre-cli-services-rebase`; build passes after import fixes. Branch is `ralph/logging-rebuild` — already exists with files in working tree, just commit.

### US-003 — Generate migration worklist

Produce `tasks/logging-migration-backlog.md`: a table of every direct `Console.*` call site (139 currently) with file path, line, current call, classification (`Result` | `Diagnostic` | `Diff` | `Error` | `Color` | `Marker` | `Input` | `Setup`), recommended replacement, BLOCKER tag if any, and worker batch assignment (1–6). Prefix the document with the classification rules so workers can apply them mechanically. Use `rt find-pattern` / `rt find-refs` against `RoslynTools.slnx` to confirm site count.

This document is the contract that US-005 through US-010 workers consume.

### US-004 — Pilot `rt replace-call`

Run `rt replace-call --from "Console.WriteLine" --to "RtConsole.WriteLine" --solution RoslynTools.slnx --dry-run` against `src/RoslynTools/Services/AddDependencyService.cs` (small, ~3 calls). Verify the diff: formatting preserved, correct overload selected, no garbled syntax. If green, set worker policy to "use `rt replace-call` for mechanical bulk after manual classification." If red, log to `tasks/dogfood-gaps.md` and set worker policy to "direct edits only."

### US-005 — Migrate Services batch A

Migrate Console.* call sites in: `Services/AddDependencyService.cs`, `Services/AddNullChecksService.cs`, `Services/AddTestService.cs`, `Services/ChangeSignatureService.cs`, `Services/FindCallersService.cs`, `Services/FindRefsService.cs`, `Services/FixNamespaceService.cs`. ~14 calls. Per-call replacement follows the worklist from US-003. Each file's tests-in-scope remain green. No `Console.*` references remain in these files at the end. Honor the BLOCKER 2 rule for any "no-op result" message on a mutation success path.

### US-006 — Migrate Services batch B

Same as US-005, for the rest of `Services/*`: `MakeAsyncService.cs`, `MakeSealedService.cs` (if present), `ReplaceCallService.cs`, `ReplaceTypeService.cs`, `SafeDeleteService.cs`, `InspectService.cs`, `OutlineService.cs`, plus any remaining services not in batch A. ~14 calls.

### US-007 — Migrate Core (small)

Migrate Console.* call sites in: `Core/MakeInstanceCore.cs`, `Core/MoveCore.cs`, `Core/MethodExtractorDuplicates.cs`, `Core/NarrowDependencyCore.cs`, `Core/ImplementInterfaceCore.cs`, `Core/MakeSealedCore.cs`, `Core/ReplaceCallCore.cs`, `Core/ConvertToRecordCore.cs`. ~30 calls. Honor BLOCKER 2 for the "is already sealed", "is already async", "is already a record", "does not implement any interfaces", "already implements all members", "No stubs to generate", "No types found in namespace", "No calls to … found" messages — these are mutation-command results and route to `RtOutputContext.Result`.

### US-008 — Migrate Core (SolutionWriter, ~12 calls)

Migrate Console.* in `Core/SolutionWriter.cs`. Special-case: this file emits both unified-diff lines (`---`, `+++`, `@@`, `[dry-run]` markers — Diff context, stdout) and progress/result lines (Result or Diagnostic). Classify each call carefully. **Excludes `WorkspaceLoader.cs` — that is BLOCKER 1, handled by US-009.**

### US-009 — Migrate Cli/Commands (non-DumpHelp)

Migrate remaining Console.* calls in `src/RoslynTools.Cli/Commands/*.cs` excluding `DumpHelpCommand.cs`. ~17 calls. These are mostly thin command wrappers; output should usually be minimal because business logic moved to Services in PR #1. Confirm any output is at the right channel (Result for command output, Diagnostic for progress).

### US-010 — Migrate `DumpHelpCommand.cs` (color output)

`src/RoslynTools.Cli/Commands/DumpHelpCommand.cs` has 23 Console.* calls including `Console.ForegroundColor` / `Console.ResetColor` for terminal colors. Migrate all to `RtConsole.WriteLine(context, msg, color)`. Preserve the `Console.IsOutputRedirected` check (move to `RtConsole.IsOutputRedirected`) that disables color in piped contexts. The help command outputs to stdout (Result context).

### US-011 — Apply BLOCKER 1 + verify BLOCKER 2 + re-enable architecture gate

`src/RoslynTools/Core/WorkspaceLoader.cs`: when emitting marker lines (`---`, `[workspace-failures]`, `[workspace-warnings]`, `  Kind: Message`), bypass the `[WARNING] ` prefix that `RtLog.Warning` injects. Use `RtConsole.WriteLine(RtOutputContext.Diagnostic, …)` for those four patterns instead — still goes to stderr but raw, no prefix. Add a regression test in `LoggingRefactorTests.cs` asserting marker lines start with `---` / `[workspace-warnings]` not `[WARNING] ---`.

Sweep the migrated codebase for the 12 BLOCKER-2 no-op result messages. Each must resolve through `RtConsole.WriteLine(RtOutputContext.Result, …)`. Add a regression test for at least 3 representative cases (e.g. "is already sealed", "is already async", "No null checks needed") that exercises the command end-to-end and asserts the message lands on the Result context, not LogEntries.

Remove `[Trait("Migration", "DeferEnforcement")]` from `ConsoleGateTests` and ensure it passes — i.e. zero direct `Console.*` references outside `RtConsole.cs`.

### US-012 — Smoke tests + ship

After all prior stories pass:
- `dotnet build "RoslynTools.slnx"` — 0 errors, 0 warnings.
- `dotnet format "RoslynTools.slnx" --verify-no-changes` passes.
- `dotnet test "RoslynTools.slnx" -v minimal` — full suite green.
- Smoke tests against `RoslynTools.slnx`:
  - `rt make-sealed RoslynTools.Models.SymbolMatch --solution RoslynTools.slnx --dry-run > out.txt` — `out.txt` is non-empty.
  - `rt inspect "RoslynTools.Core.WorkspaceLoader" --solution RoslynTools.slnx | jq '.'` — succeeds (valid JSON on stdout).
  - Inject a deliberately bad workspace condition (or use an existing test fixture), run any command, confirm stderr emits `[workspace-warnings]` raw without `[WARNING] ` prefix.

Push `ralph/logging-rebuild`, open PR → main with title `feat(logging): IRtConsole/RtLog abstraction + Console.* migration` and body that lists the 3 BLOCKER fixes inline.

## Story dependencies

```
US-001 (fix main)                              ← lands first as separate PR
   ↓ (merge to main; rebase ralph/logging-rebuild on top)
US-002 (infrastructure)                        ← single commit on ralph/logging-rebuild
   ↓
US-003 (worklist) ─→ US-004 (rt pilot)
   ↓
US-005 ─┬─ US-006 ─┬─ US-007 ─┬─ US-008 ─┬─ US-009 ─┬─ US-010   ← parallel via /execute
   └────┴──────────┴──────────┴──────────┴──────────┘
   ↓
US-011 (BLOCKERs + gate)                       ← Opus pass, sequential after swarm
   ↓
US-012 (smoke + PR)
```

## Constraints

- **No force-push to main.** Phase A lands fix-forward.
- **Tag `pre-cli-services-rebase` is preserved** until US-012's PR merges. Decide afterward via `/cleanup`.
- **`--no-verify` on commits is forbidden** unless explicitly authorized.
- **Worker hard gate: tests in scope must pass before a worker commits.** No assertion weakening to make mutants/tests die. If a worker can't keep its scope green, it leaves the failure with a comment and surfaces it in the report.
- **Partition is by file**: no two workers touch the same file. Reconciliation in US-011 merges in dependency order (Core → Services → Cli/Commands).
- **`StructuredLoggingTests`** (template-literal gate) is enforced from US-002 onward. Workers cannot use `RtLog.X($"interp {string}")` — must use `RtLog.X("template {Field}", field)`.
- **CLAUDE.md output convention** is binding:
  - Read-only inspection → JSON to stdout.
  - Mutation result → plain text to stdout.
  - Diagnostics/progress/error → stderr.
  - Dry-run diff → stdout (Diff context).

## Open questions / risks

- **`rt replace-call` correctness on this codebase** — answered by US-004 pilot. If pilot fails, swarm cost goes up; not a project blocker.
- **Worker conflicts on cross-file refactors** — partition is file-level; if a Service references a Core helper that another worker is migrating, ordering matters. Mitigation: workers operate on their own files only and don't touch the new infrastructure (it's already in place).
- **`LoggingRefactorTests.cs` import fixes in US-002** — depend on what PR #1 renamed. May require a small triage pass; budget 15 minutes.
- **Unused-using cleanup** during migration — workers should drop `using System;` lines that become unused once `Console.*` refs leave the file. Don't add them where they aren't needed.

## How to convert this PRD

```
/task convert ralph/prd-logging-rebuild.md
```

This expands each US-xxx above into the JSON shape with full acceptance criteria, then writes to `ralph/prd.json`. The current `ralph/prd.json` (the change-signature backlog, all `passes: true`) should be archived as part of the merge of its branch — that's a `/check merge` step on the existing branch, not part of this PRD.
