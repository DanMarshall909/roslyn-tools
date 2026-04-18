- 2026-04-17: Replace Console.Error.WriteLine with ILogger across multiple partial controller files — rt has no general pattern-replace command (only replace-datetime is supported)

## dependency-graph gap (2026-04-18)

- Graph edges only track fields, method params/returns, base types, and interfaces — misses local variable usages inside method bodies. Command classes (static, no fields) appear as isolated nodes even when they use WorkspaceLoader/SolutionWriter internally. Fix: add body-level IdentifierNameSyntax walk to find local variable type references.

## extract-method --replace-duplicates known issues (US-016, 2026-04-19)

- `MethodExtractorDuplicates.ReplaceDuplicatesAsync` computes the match list once up-front against the extracted solution, then mutates the solution per-match by line-range selection. When multiple matches live in the same file, later line ranges can drift as the file is rewritten; `SelectStatementsInRange` may then pick the wrong statements. Fix: either (a) process matches file-by-file bottom-up so earlier edits don't affect later line numbers, (b) re-run `FindMatchesAsync` after each replacement, or (c) carry the exact match node set from `PatternMatcher` through to replacement via `SyntaxAnnotation`.
- `ExtractMethodCommand.SetHandler` lambda is ~40 lines, exceeding the "Command ExecuteAsync ≤ 20 lines" thin-shell rule introduced by this PRD. Fix: extract an `ExtractMethodHandler.RunAsync(ctx)` helper in Core.

## bisect hazard (2026-04-19)

- Commit `0445c01` (US-012) adds `--replace-duplicates` wiring to `ExtractMethodCommand.cs` that forwards to a `MethodExtractor.ExtractAsync(replaceDuplicates: ...)` parameter which did not exist until commit `31765bc` (US-016). Commit `0445c01` therefore does not build standalone. Caused by cross-worktree file pollution during ralph/execute parallel batches. Fix: interactive rebase to move the ExtractMethodCommand.cs chunk from US-012's commit into US-016's. Deferred because main builds at HEAD and all 168 tests pass — but a `git bisect` across this range will fail spuriously.

## inline-method known issues (US-002, 2026-04-17)

- Method removal after inlining scopes by identifier text only — will remove wrong method if same name appears in different class in same file. Fix: scope to containing TypeDeclarationSyntax by type name + param count.
- ParameterSubstitutor does not parenthesize substituted arguments — `Double(a+b)` inlines as `a+b*2` not `(a+b)*2`. Fix: wrap non-trivial argument expressions in ParenthesizedExpressionSyntax.
- 2026-04-18: outline TourStore.Auth namespace with --solution flag — System.ArgumentNullException at BuildHostProcessManager.CreateDotNetCoreBuildHostStartInfo; rt needs DOTNET_SDK or MSBuild path set when invoked outside solution directory context
  - **FIXED 2026-04-18**: root cause was single-file publish. Roslyn's BuildHostProcessManager uses `typeof(...).Assembly.Location` which is empty for bundled managed assemblies, so `Path.Combine(null, "BuildHost-netcore", ...)` threw. Added `<IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>` to the publish PropertyGroup — restores Assembly.Location by extracting bundled content to a temp dir on startup. Verified via `cmd //c rt.cmd inspect/list-smells --solution ...`.
