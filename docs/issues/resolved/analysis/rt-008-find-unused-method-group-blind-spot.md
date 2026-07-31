---
tags:
  - analysis
  - find-unused
  - false-positive
ticket: rt-008
severity: High
status: Resolved
area: analysis
owner: ""
reported: 2026-07-31
summary: "find_unused reports high-confidence false positives for methods reachable only via method-group-to-delegate conversion (every *Command.Build)"
---

# find_unused misses method-group delegate references

## Context

Running `rt`'s own `find_unused` against `RoslynTools.slnx` reports **66
high-confidence "unused" methods**, and ~44 of them are the `Build()` method on
every `*Command` (`AddDependencyCommand.Build`, `ComplexityCommand.Build`, …).

These are **not** dead. Every one of these commands runs in production `rt`.
They are referenced through a method-group-to-delegate conversion in
`CommandDescriptorRegistry` — `Func<Command> BuildCliCommand` populated via
`entry.Build` — which the reference walker in `FindUnusedService.AnalyzeAsync`
does not follow. The result is a **systematic blind spot**: any method reachable
only as a method group assigned into a delegate / data table is scored
"high confidence, no references."

This matters more than a normal false positive because `find_unused` suggests
`rt refactor safe-delete` for each hit. A user trusting the high-confidence
label could delete the entire command surface.

The non-`Build` hits in the same report (`RtCommandFlags.WithType/WithFile/…`
fluent builders, `CommandOptionFactory.CreateNullableStringOption`) still need
triage — some may be genuinely unwired — but they must be assessed *after* the
method-group blind spot is fixed, because the fix changes the result set.

## Reproduction

```powershell
rt analyze find-unused --scope all --solution "RoslynTools.slnx"
```

Observe `*Command.Build` methods listed with `confidence: high`,
`reason: "No source references were found."`

## Acceptance Criteria

- [x] `find_unused`'s reference walker treats a method-group conversion to a
      delegate (including into an array/collection initializer and constructor
      argument) as a reference. — Superseded: the actual mechanism is not a
      method-group conversion (see Root cause below). `ShouldExclude` now
      recognizes the real mechanism instead.
- [x] Re-running the analysis no longer reports any `*Command.Build` as unused.
- [x] Genuinely-unwired members are still reported (the fix must not blanket-
      suppress — verify at least one real dead method is still flagged).
- [x] Remaining flagged members from the original run are triaged: wired-up vs
      genuinely dead recorded in the decision log.
- [ ] Guardrail test tracked in rt-009 passes against the fixed analyzer. —
      rt-009 is its own ticket; the red/green test added here
      (`FindUnused.excludes_reflectively_invoked_command_build_methods`) covers
      the same rule but rt-009's broader "enumerate every command descriptor"
      guardrail is still open.

## Root cause (revised)

The ticket as filed described the mechanism as a method-group-to-delegate
conversion (`entry.Build` in `CommandDescriptorRegistry`). Tracing it back:
`entry` there is a tuple field already populated by `App.Discover()`, which
does not do a method-group conversion at all — it calls
`type.GetMethod("Build", ...)` and `MethodInfo.Invoke(...)` via
`System.Reflection`. `IRtCommand` doesn't even declare `Build()`; every
implementer's `Build()` is a plain method found and invoked purely
reflectively. There is no method-group conversion anywhere in the path, so a
reference-walker change to catch method-group conversions would not have
fixed the reported bug.

`FindUnusedService` already had the concept this needed: type-scope findings
in a project that calls `GetTypes()`/`IsAssignableFrom()` (rt's own
`App.Discover()` matches `ReflectiveDiscoveryApis`) are downgraded to low
confidence rather than reported as dead. That downgrade path only covered
types, not methods, and `App.Discover()`'s `Build()` contract is precise
enough (public `Build()` on any `IRtCommand` implementer, matched by
`type.GetMethod("Build", ...)`) that — like `Main` and `[Fact]`/`[Theory]` —
it can be excluded outright rather than downgraded, since we know the exact
consumer and it lives inside this same repo.

## Fix

Added `ShouldExclude` handling in `FindUnusedService`: a public method named
`Build` on a type implementing `IRtCommand` is excluded, mirroring the
existing `Main`/`[Fact]`/`[Theory]` exclusions rather than the type-scope
low-confidence downgrade.

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-07-31 | Split the analyzer fix (this ticket) from its guardrail test (rt-009) | The test is the durable regression guard; it should land red against today's analyzer and green after this fix. |
| 2026-07-31 | Root cause is reflective invocation (`MethodInfo.Invoke`), not a method-group-to-delegate conversion as originally filed | Traced `App.Discover()`: `IRtCommand` declares no `Build()` member; every implementer's `Build()` is located via `Type.GetMethod` and invoked via `MethodInfo.Invoke`. No method-group conversion exists anywhere on the path. |
| 2026-07-31 | Fixed via a new `ShouldExclude` rule (public `Build()` on an `IRtCommand` implementer), not via the type-scope low-confidence downgrade | The existing low-confidence path exists because a generic assembly scan could plausibly pick up *any* instantiable type — genuine uncertainty. `App.Discover()`'s `Build()` contract is exact and known (same repo), so — like `Main` and `[Fact]`/`[Theory]` — full exclusion is correct, not a downgrade. This also satisfies the "no longer reports `*Command.Build`" AC literally: a low-confidence downgrade would still have shown up in results. |
| 2026-07-31 | Triaged the 35 residual `scope=methods` findings after the fix | Categorized below. No wholesale safe-delete was performed — that's a separate, reviewable action, not part of this analyzer-bug ticket. |

### Triage of residual findings (post-fix, `rt inspect find-unused --scope methods`)

**Confirmed genuinely dead (zero references, spot-checked `RtCommandFlagBinder.WithType` directly via `find-refs`; same shape for the rest — plain private/internal helpers with no interface or override backing):**
`RtCommandFlagBinder.WithType/WithFile/WithJson/WithFormat/WithVerbose/WithDecisions/WithApplySkip`
(`RtCommandFlags.cs`), `CommandOptionFactory.CreateNullableStringOption`,
`DaemonPipeConsole.Write`, `JsonSchemaBuilder.String`/`Boolean`
(`ToolRegistry.cs`), `MethodExtractorDuplicates.UpdateSourceFileCallSiteAsync`,
`ExtractClassService.RunAsync`, `SymbolResolutionService`/
`ISymbolResolutionService`'s `TryFindSymbolByFqnAsync`/`TryFindTypeByFqnAsync`/
`TryFindMethodBySpecAsync` trio, plus several test-only helpers
(`AdhocProjectBuilder.ReferenceProject`, `Complexity.FindMethod`,
`FindDuplicatesMigration.BuildDuplicateProject`,
`PipedSymbolReaderTests.Read`, `SolutionWriterIntegration.LoadTextAndVersionAsync`,
`MsBuildFixture.Initialize`, `MoveMethod.TextAsync`,
`TransformParameters.FilePath`). Candidates for a future safe-delete pass;
not deleted here.

**A second, distinct analyzer blind spot — not genuinely dead, filed as [[rt-011]]:**
`BlockNormalizer.VisitToken` and `IdentifierVariationDetector`'s
`VisitVariableDeclarator` (both `override` members of Roslyn's
`CSharpSyntaxRewriter`/`CSharpSyntaxWalker`, dispatched from inside
`Microsoft.CodeAnalysis.CSharp.dll`), `RtConsoleLogger<T>.BeginScope` and the
test double `CapturingLogger<T>.BeginScope` (both implement `ILogger<T>`,
dispatched from `Microsoft.Extensions.Logging` internals), and
`StubSymbolResolutionService`'s interface members (test double for
`ISymbolResolutionService`). All are invoked only through interface/virtual
dispatch from a caller outside the analyzed solution's source — a different
mechanism from this ticket's reflective-invocation bug, so it gets its own
fix rather than reusing the `IRtCommand.Build` exclusion.

**Test-runner-lifecycle methods, likely a narrower instance of the rt-011 shape (not independently re-filed):**
`PublicCoreIsDocumented`/`SmellFreeNamespaces`'s `InitializeAsync`/
`DisposeAsync` (both implement `IAsyncLifetime`, dispatched by xunit).
