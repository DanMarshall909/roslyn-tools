---
tags:
  - analysis
  - find-unused
  - false-positive
ticket: rt-011
severity: Medium
status: Open
area: analysis
owner: ""
reported: 2026-07-31
summary: "find_unused reports false positives for methods invoked only via interface/virtual-override dispatch from a caller outside the analyzed solution (ILogger.BeginScope, CSharpSyntaxRewriter/Walker overrides, test-double interface implementations)"
---

# find_unused misses interface- and override-dispatch references

## Context

Triaging the residual findings after the rt-008 fix (method-group/reflective
`IRtCommand.Build` blind spot) surfaced a second, distinct blind spot while
checking whether the remaining hits were genuinely dead:

- `BlockNormalizer.VisitToken` and the nested walker's
  `IdentifierVariationDetector.VisitVariableDeclarator` — both `public override`
  members of `CSharpSyntaxRewriter`/`CSharpSyntaxWalker`. The dispatcher that
  calls them (`Visit(node)`) lives inside `Microsoft.CodeAnalysis.CSharp.dll`,
  not in this solution's source, so `SymbolFinder.FindReferencesAsync` can never
  see the call site.
- `RtConsoleLogger<T>.BeginScope` and the test double
  `RoslynTools.Tests.Mutation.CapturingLogger<T>.BeginScope` — both implement
  `ILogger<T>.BeginScope`, called by `Microsoft.Extensions.Logging` internals,
  again outside the solution.
- `RoslynTools.Tests.Mutation.StubSymbolResolutionService` (a hand-rolled test
  double implementing `ISymbolResolutionService`) reports several members
  unused; unlike the two cases above the caller *is* in-solution (the service
  under test), so this one needs checking case-by-case rather than assumed —
  see Acceptance Criteria.

The common shape: a method whose only caller reaches it by dispatch through an
interface or base-class reference, where that caller is either outside the
loaded solution (framework/BCL) or reached only via DI substitution the
analyzer's syntax walk cannot trace. `ShouldExclude` currently only skips
*interface member declarations themselves* (`symbol.ContainingType?.TypeKind ==
TypeKind.Interface`); it has no handling for *implementations*.

This is a different mechanism from rt-008 (reflection via `MethodInfo.Invoke`
with zero source syntax at all) — here there genuinely is calling syntax, it's
just outside the compiled solution — so it needs its own fix, not a reuse of
the `IRtCommand.Build` exclusion.

## Acceptance Criteria

- [ ] Decide and document the detection strategy: e.g. exclude a method that
      overrides a `virtual`/`abstract` member or implements an interface member
      declared in a referenced assembly outside the solution (BCL/NuGet), vs.
      one implementing a solution-local interface (where a real reference
      might exist and should still be trusted if found).
- [ ] `BlockNormalizer.VisitToken` and
      `IdentifierVariationDetector`'s `VisitVariableDeclarator` no longer report
      as unused.
- [ ] `RtConsoleLogger<T>.BeginScope` no longer reports as unused.
- [ ] For `StubSymbolResolutionService`, determine per-member whether the
      interface method (`ISymbolResolutionService.*`) has any real caller in
      the solution; if it does, decide whether the fix's detection should
      trust that or still flag the stub (record the reasoning either way).
- [ ] Fix must not blanket-suppress every interface implementation — verify a
      genuinely dead override/implementation (hand-inject one) is still
      flagged.

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-07-31 | Filed separately from rt-008 rather than folded into that fix | Different root cause (dispatch from outside the solution vs. reflection with no call syntax at all) — reusing rt-008's `IRtCommand.Build` exclusion would not generalize to this shape. |
