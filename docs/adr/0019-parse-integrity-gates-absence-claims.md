# ADR-0019: Parse integrity gates absence claims

- Status: Accepted
- Date: 2026-09-04
- Issues: [#128](https://github.com/DanMarshall909/roslyn-tools/issues/128)
- Related: [#15](https://github.com/DanMarshall909/roslyn-tools/issues/15) (external dispatch), [#122](https://github.com/DanMarshall909/roslyn-tools/issues/122) (workspace completeness governs confidence)

## Context

`rt` pinned `Microsoft.CodeAnalysis.*` 5.3.0, which predates C# `union`
declarations. Run against a `net11.0` / `LangVersion preview` solution containing
ten of them, the parser read

```csharp
public readonly union ValueExpression(ValueExpression.Literal, ValueExpression.Dice);
```

as a bodiless method whose return type is the word `union`. The parse failure never
surfaced as a parse failure. It surfaced as analysis: `find-unused --scope methods`
reported four core domain types as unused **methods** at `confidence: "high"`, each
with a `delete-unreferenced` suggested fix, each with hundreds of real references.
Acting on any of them would have deleted the rules engine.

Two properties of Roslyn make this the default failure mode rather than an unlucky
one. Error recovery guesses at the nearest declaration it does know, and the guess is
a first-class syntax node with a first-class symbol — nothing downstream can tell it
from real source. And a guessed symbol has no references *by construction*, which is
indistinguishable from a real symbol nobody uses.

The completeness assessment that should have warned the reader made it worse. It
stopped at the first twenty error messages and broke out of the project loop, so one
noisy project consumed the whole budget and every project after it went unexamined.
`doctor` reported `Partial`, seventeen projects, and exactly twenty diagnostics, all
prefixed `DnDan.Application`. `DnDan.Domain` was equally mis-parsed and showed
nothing — so it read as having loaded clean, and its findings were trusted.

## Decision

**Absence of references is evidence only where the source was understood.** Three
mechanisms, at three different scopes:

1. **The parser is kept current with the syntax it is asked to read.**
   `Microsoft.CodeAnalysis.*` moves to 5.9.0, which parses `union` as a
   `UnionDeclarationSyntax` — a `TypeDeclarationSyntax`, so `find-unused` classifies
   it through the type path it already had, with no new special case.
   `Microsoft.Extensions.*` follows to 10.0.1 because 5.9.0 requires it.

2. **A document carrying a syntax error makes no absence claim at all.**
   `FindUnusedService` skips it entirely rather than reporting what the parser
   guessed. This holds for well-formed-looking declarations in the same document
   too: recovery discards source, and the discarded source is exactly where a
   reference would have been.

3. **A symbol is reported as an unused method only where the declaration site really
   declares a method.** A method with no body must say why it has none — `abstract`,
   `extern`, `partial`, or interface membership. Anything else is a fragment the
   parser could not otherwise place. This is not redundant with (2): a bodiless
   declaration is a *semantic* error, so the tree carries no syntax diagnostic and
   the document-level refusal never sees it.

And, for the report a reader uses to decide what to trust: **the completeness
assessment names every project that failed to compile, and states what it left out.**
The message budget is shared equally between failing projects so a noisy one cannot
crowd out its neighbours; where more projects failed than the budget has lines, each
still gets one, because a project absent from that list reads as a project that
compiled.

Upgrading the parser is the only one of these that fixes today's instance. The other
three are what remains true when the next preview feature arrives, because a parser
is always one feature behind something.

## Consequences

- `find-unused` will report fewer findings on repositories using syntax `rt`'s Roslyn
  does not know, and will silently report none from an affected document. That is the
  intended trade: a missing finding costs a search, a wrong one costs live code.
- A repository that keeps a genuinely broken file in the solution loses `find-unused`
  coverage of that file until it compiles. Acceptable — the analysis was never
  meaningful there.
- `doctor` now compiles every project rather than stopping at a full diagnostic
  budget, so assessment cost scales with solution size instead of with the first
  noisy project.
- Diagnostic *text* is not a contract; the assessment's list is a human- and
  agent-readable summary and its shape may change again.
- Deliberately deferred: `find-unused` does not yet say **which** documents it
  declined to analyse. Explaining missing semantic scope inside the find-unused result
  is an acceptance criterion of #122 and belongs with the workspace contract that
  issue introduces, not a second bespoke channel here.
- Also out of scope: the residual false positives on the same run — `Read`/`Write`,
  `Initialize`, `ExecuteAsync`, `SendAsync`, `ConfigureWebHost` — are external
  interface and override dispatch, which is #15.

Enforcement is by test, not review: each guard is pinned by a test that fails when
that guard alone is removed, and a `[Theory]` pins that `abstract`, `extern` and
`partial` methods are still analysed, so the method guard cannot quietly become a
blind spot covering every abstraction in a solution.

## Alternatives considered

- **Upgrade Roslyn and stop.** Rejected: it fixes this syntax and leaves the failure
  mode intact for the next one.
- **Report mis-parsed declarations at low confidence instead of withholding them.**
  Rejected: low confidence is the right answer for a symbol the analysis can see but
  cannot see *through*. Here rt does not know what the symbol is; naming a fragment at
  any confidence asserts a parse that did not happen.
- **Treat a syntax error anywhere as poisoning absence claims across the whole
  solution.** Correct, and larger than this issue: a lost reference could have been to
  any symbol. That is the workspace-wide confidence policy #122 owns.
- **Detect the union keyword specifically.** Rejected: a special case per preview
  feature, wrong the moment the next one ships.
- **Raise the diagnostic limit.** Rejected: a bigger round number is still a round
  number the reader mistakes for the whole truth.

## References

- Related ADRs: [ADR-0001](0001-core-service-thin-host-adapters.md)
- Related docs: [architecture overview](../architecture-overview.md)
