- 2026-04-17: Replace Console.Error.WriteLine with ILogger across multiple partial controller files — rt has no general pattern-replace command (only replace-datetime is supported)

## dependency-graph gap (2026-04-18)

- Graph edges only track fields, method params/returns, base types, and interfaces — misses local variable usages inside method bodies. Command classes (static, no fields) appear as isolated nodes even when they use WorkspaceLoader/SolutionWriter internally. Fix: add body-level IdentifierNameSyntax walk to find local variable type references.

## inline-method known issues (US-002, 2026-04-17)

- Method removal after inlining scopes by identifier text only — will remove wrong method if same name appears in different class in same file. Fix: scope to containing TypeDeclarationSyntax by type name + param count.
- ParameterSubstitutor does not parenthesize substituted arguments — `Double(a+b)` inlines as `a+b*2` not `(a+b)*2`. Fix: wrap non-trivial argument expressions in ParenthesizedExpressionSyntax.
- 2026-04-18: outline TourStore.Auth namespace with --solution flag — System.ArgumentNullException at BuildHostProcessManager.CreateDotNetCoreBuildHostStartInfo; rt needs DOTNET_SDK or MSBuild path set when invoked outside solution directory context
  - **FIXED 2026-04-18**: root cause was single-file publish. Roslyn's BuildHostProcessManager uses `typeof(...).Assembly.Location` which is empty for bundled managed assemblies, so `Path.Combine(null, "BuildHost-netcore", ...)` threw. Added `<IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>` to the publish PropertyGroup — restores Assembly.Location by extracting bundled content to a temp dir on startup. Verified via `cmd //c rt.cmd inspect/list-smells --solution ...`.
