# Agent Playbook

Detailed operating guidance for agentic coding sessions. Keep root entrypoints
small: `AGENTS.md` is the shared repository guide, and `CLAUDE.md` only imports
it.

## Architecture Workflow

Every feature follows the Core service + thin host adapters pattern. Put logic in
`src/RoslynTools/Services/`; keep CLI commands and MCP handlers as adapters that
parse inputs, call the same service through DI, and render host-specific output.

When an architectural decision is taken, write an ADR under `docs/adr/`, update
`docs/architecture-overview.md`, update the most specific companion doc, and
commit those changes together as `docs(adr): NNNN <title>`.

## Dogfooding

When doing C# refactoring, ask whether `rt` can perform the change. Use the tool
first for symbol rename, move, reference discovery, and class restructuring. If
`rt` cannot do it or produces poor output, log the gap or fix it.

After modifying or adding a command, smoke test it against `RoslynTools.slnx`:

```powershell
rt inspect describe "RoslynTools.Core.WorkspaceLoader" --solution RoslynTools.slnx
rt inspect outline src/RoslynTools/Core/SymbolLocator.cs --solution RoslynTools.slnx
rt inspect relationships references WorkspaceLoader --solution RoslynTools.slnx
rt refactor rename "RoslynTools.Models.SymbolMatch.DisplayName" NewName --solution RoslynTools.slnx --dry-run
```

Check exit code, stdout/stderr separation, sensible dry-run diffs, and workspace
load warnings.

## Continuous reflection

Apply the shared Reflection rule and `reflect` procedure throughout the task.
Watch for dry-run edits, preview/apply differences, incomplete semantic results,
misleading discovery, oversized responses, repeated calls, missing operations
and manual fallback work.

Deduplicate actionable findings in the existing task or maintainer feedback
record (`rt maintainer feedback add`). Include the command and revision,
expected and observed behavior, minimal reproduction, workaround and proposed
acceptance criterion. Measure response bytes and call counts when assessing
cost; label token estimates as estimates. Preserve completeness and safety
diagnostics when selecting a smaller result payload.

Record feedback between operations, never between the final clean-commit check
and applying a confirmed refactoring. Continue the task unless a finding
threatens current correctness or safety, invalidates required evidence, or
blocks meaningful progress. Present material findings together when control
returns to the user. Propose future standing-guidance changes for owner review;
implement those already authorized by the task.

## Compact discovery and safe application

Start with `rt prime`, then `rt suggest "<intent>" --top 3 --llm`, then the
chosen command's `--help`. Use `rt dump-help --compact` when the task needs a
wider catalogue; reserve the full dump for reference generation. Use JSON when
machine parsing is needed. Choose a bounded file, symbol, project or result
limit; do not hide partial-workspace warnings to make results look complete.

Before every refactoring, require `git status --porcelain=v1 --untracked-files=all`
to be empty and record `git rev-parse HEAD`. Run the supported preview, inspect
its complete proposed changes, and require unchanged HEAD plus empty status
again immediately before applying. Unexpected edits are defect evidence: keep
them and investigate before application. Test, review and commit the coherent
change before starting another refactoring. Manual extractions use the same
clean checkpoints and caller/behavior evidence.

Inspect each command's apply controls. Some mutators default to preview and
require `--dry-run false` to write; omitting the flag still previews. Durable
prepare/apply operations have their own reviewed-plan protocol.

## Parallel Safety

`rt` is designed for parallel agent worktrees. Known guarded race sites:

- `rt maintainer feedback add`: `FeedbackStore.Append` serializes JSON writes with an
  in-process semaphore plus `FileShare.None` lock.
- Wrapper rebuilds: `rt.ps1` uses a named mutex; `rt.sh` uses `flock`.
- `scripts/deploy-skill.ps1`: retries copy operations and exits cleanly after
  contention.
- MSBuild `obj/`: set `RT_ISOLATED_OBJ=1` so workspace loads use per-process
  intermediate output under temp.

If a new race appears, capture it in a parallel xUnit regression test before
fixing it.

## Build purity

Run `node scripts/check-build-purity.mjs` when changing build targets or the
restore/build/test workflow. It snapshots authored checkout files, including
ignored `docs/COMMANDS.md`, and a disposable user profile around separate plain
restore, Debug build, and test phases. The detector self-tests are
`node --test tests/build-purity.test.mjs`. Normal `bin`/`obj` and other declared
output directories, the SDK's CRL cache, and a disposable NuGet/cache/temp area
are exempt; the gate
rejects tracked files placed under those exempt directories. The current CI job
provides Linux evidence. Windows special-folder isolation and full OS matrix
coverage remain P1.8 work.

## Command Metadata Contract

Every CLI command class ending in `Command` must implement `IRtCommand`.

- Add a command as `internal sealed class FooCommand : IRtCommand`.
- Keep `Info.Name` aligned with `new Command("name", ...)`.
- Set `Info.Category` to `inspect`, `analyze`, or `refactor`.
- Update `Info.Examples` when flags or invocation shape changes.
- Keep `Info.Mutates` and `Info.JsonOutput` accurate.

After command changes, build the CLI project, run `npm run generate:commands`,
and commit only source/doc files intended for version control. Generated
`docs/COMMANDS.md` is local convenience output; ordinary builds do not write it.

## Result and Output Conventions

Core services return `Result<T, RtError>` for expected user-facing failures such
as not found, ambiguous match, or invalid input. Throw only for invariant
violations and unexpected failures.

CLI commands surface errors through `RtErrorPresenter.Surface`; MCP handlers map
them to error `ToolResult`s. `RtResult<T>` is the output envelope for renderers,
not the same abstraction as `Result<T, RtError>`.

Output conventions:

- Read-only inspection commands: JSON to stdout.
- Mutation commands: plain text to stdout.
- All commands: diagnostics and progress to stderr.

Exit codes:

- `0`: success
- `1`: error
- `2`: decisions required

## Decisions Files

Use `--decisions <path>` for machine-driven symbol disambiguation.

1. First run writes pending choices to YAML and exits `2`.
2. Fill each `choose:` value with the selected option index.
3. Re-run the same command to apply the saved choices without prompting.

The schema lives under `src/RoslynTools/Core/Decisions/`.

## Mutation Testing

Stryker.NET is pinned in `.config/dotnet-tools.json` and configured by
`stryker-config.json`.

```powershell
dotnet tool restore
dotnet stryker
dotnet stryker --config-file stryker-adapters-config.json
dotnet stryker --config-file stryker-mcp-schema-config.json
```

Each configuration targets one production assembly. The adapter and MCP schema
configurations select owning in-process tests and exclude spawned-process
journeys. Moving code across projects requires changing the mutation target;
a glob under the Core configuration cannot mutate an outer assembly.

For surviving mutants, choose one valid response: add the missing test, delete
dead code, or document a genuinely equivalent mutant and add it to the ignore
configuration. Do not weaken assertions to improve score.

## Refactoring Philosophy

Consolidate duplication only when it removes real maintenance pain. Use patterns
as a lens, not a mandate. Prefer the smallest abstraction that makes repeated
code clearer; avoid adding factories, strategies, builders, or hierarchies for
two or three similar call sites.

Use `rt analyze find-duplicates --solution RoslynTools.slnx` to find candidates,
then inspect high-scoring groups before changing design.

## Dependencies

| Package | Purpose |
|---|---|
| `System.CommandLine` | CLI parsing |
| `Microsoft.Build.Locator` | Find MSBuild installation |
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` | Load solutions |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | C# syntax and semantic analysis |
| `DiffPlex` | Unified diff output for `--dry-run` |
| `YamlDotNet` | Read and write `--decisions` files |
