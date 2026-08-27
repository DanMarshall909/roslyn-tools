# Session: 2026-08-27 — Language Feature Density

**Goal:** Design an rt command that inventories C# language features used by a
solution and quantifies their density for refactoring decisions.
**Solution:** RoslynTools.slnx

---

## Step 1 — Establish the command family [REAL]

`rt dump-help --json`

The live catalogue contains `analyze complexity`, `analyze list-smells`, and
`analyze solid` as static-analysis reports. The migration playbook classifies that
shape as R3: daemon-preferred, read-only, typed output. The new command extends R3
and must be born on the host-neutral application-port pattern from ADR-0007.

The repository currently has 502 C# source documents under `src/` and `tests/`.
Simple source probes found widespread file-scoped namespaces, records, collection
expressions, raw strings, required/init members, async/await, and smaller pockets of
switch/with expressions. These probes establish that the report would have useful
variation, but they are not the proposed implementation: text matching is not a
reliable language-feature detector.

---

## Step 2 — Aggregate inventory [SIMULATED]

`rt analyze language-features --solution RoslynTools.slnx`

<!-- SIMULATED — command does not exist yet -->

```text
C# language features: RoslynTools.slnx
Catalog: csharp-v1 | 4 projects | 502 files | 73,821 non-blank source lines

Feature                    Since   Uses   Files   /KLOC   File %   Adoption
file_scoped_namespace      10.0     497     497    6.73    99.0%   99.2% of namespace declarations
record                      9.0     283     136    3.83    27.1%   —
raw_string_literal         11.0   1,186     114   16.07    22.7%   —
async_method                5.0   1,305     247   17.68    49.2%   —
collection_expression      12.0     129      33    1.75     6.6%   —
required_member            11.0     167      30    2.26     6.0%   —
init_accessor               9.0     242      29    3.28     5.8%   14.7% of writable accessors
with_expression             9.0      10       8    0.14     1.6%   —
switch_expression           8.0       4       2    0.05     0.4%   50.0% of switch constructs

Locations omitted. Use --feature <id> --include-locations to inspect occurrences.
Counts shown here are protocol examples and are not authoritative scan results.
```

The table sorts by occurrence count by default. `--top 0` returns every catalogue
feature in the typed result; text rendering may use `--top` for a bounded view.

---

## Step 3 — Stable JSON contract [SIMULATED]

`rt analyze language-features --solution RoslynTools.slnx --feature collection_expression --json`

<!-- SIMULATED — command does not exist yet -->

```json
{
  "catalogVersion": "csharp-v1",
  "generatedCode": "excluded",
  "totals": {
    "projects": 4,
    "files": 502,
    "sourceLines": 73821
  },
  "projects": [
    {
      "name": "RoslynTools",
      "languageVersion": "latest",
      "effectiveLanguageVersion": "14.0"
    }
  ],
  "features": [
    {
      "id": "collection_expression",
      "name": "Collection expression",
      "category": "expression",
      "introducedIn": "12.0",
      "detectorKind": "syntax",
      "countingBasis": "one collection expression syntax node",
      "occurrences": 129,
      "files": 33,
      "occurrencesPerKloc": 1.747,
      "filePercent": 6.574,
      "adoption": null,
      "locations": null
    }
  ],
  "diagnostics": []
}
```

Raw occurrence counts remain integers. Derived rates use the solution-wide analyzed
line/file totals so filters do not change their denominators.

---

## Step 4 — Refactoring drill-down [SIMULATED]

`rt analyze language-features --feature file_scoped_namespace --include-locations --solution RoslynTools.slnx --json`

<!-- SIMULATED — command does not exist yet -->

```json
{
  "features": [
    {
      "id": "file_scoped_namespace",
      "occurrences": 497,
      "adoption": {
        "numerator": 497,
        "denominator": 501,
        "opportunity": "namespace declarations",
        "percent": 99.202
      },
      "locations": [
        {
          "project": "RoslynTools",
          "path": "src/RoslynTools/Core/RtCommandInfo.cs",
          "line": 1,
          "column": 1
        }
      ]
    }
  ]
}
```

This supports two refactoring questions without making a style judgement:

1. Is a newer feature established enough to be the local convention?
2. Where are the mechanically comparable exceptions?

The first slice does not auto-rewrite those exceptions. Suggested refactors are only
attached when an existing rt command is a deterministic match.

> **[ISSUE: rt-015]** written — add catalog-backed C# language-feature density analysis.

`rt feedback add --description "rt: analyze language-features — inventory the versioned C# language features used across a solution and report normalized density, file coverage, opportunity-based adoption, and source locations for refactoring decisions" ...`

```text
Feedback recorded: FB-374
```

---

## Step 5 — Implemented and dogfooded [REAL]

`dotnet src/RoslynTools.Cli/bin/Release/net10.0/rt.dll analyze language-features --solution RoslynTools.slnx --refactorable-only --include-locations`

The final live command analyzed 5 projects, 504 files, and 73,325 non-blank source lines.
Its review-oriented result found:

- 229 `init_accessor` uses in 23 files, with 64 set-accessor review candidates;
- 71 `switch_expression` uses in 41 files, with 19 switch-statement review candidates;
- solution-relative candidate locations suitable for passing to a focused refactor.

An aggregate JSON run also measured 499 file-scoped namespace declarations (100%
adoption), 1,125 raw string literals, 927 collection expressions, 231 records, 216
target-typed `new` expressions, and 147 required members. Unsupported C# 15 syntax
detectors emitted a typed diagnostic rather than zero-valued feature rows.

## Step 6 — Verification [REAL]

- Release solution build: 0 warnings and 0 errors.
- CLI/core tests: 1,643 passed, 9 skipped, 0 failed.
- MCP tests: 61 passed, 0 failed.
- Format verification, `git diff --check`, and JSON validation passed.
- Scoped coverage: 82.33% line, 78.99% branch, 97.18% method.
- The Stryker run did not yield a valid score: configured mutate globs combined with
  the scoped filter, selected unrelated `MethodExtractor` mutants, and then failed
  safe-mode compilation. This is recorded as a tooling limitation, not a green gate.

---

## Retrospective

### Real commands used

- `dump-help` established the command family and existing surface.
- `feedback add` recorded the missing feature as `FB-374`.
- `analyze language-features` measured the repository and emitted review candidates.
- Release build, tests, formatting, coverage, and dogfood checks verified the slice.

### Simulated commands → implemented rt command

- `analyze language-features` — promoted to a daemon-preferred typed R3 report with
  generic MCP projection and a refactoring-oriented candidate view.

### Gaps logged

- C# 15 syntax detectors remain explicitly unsupported until the pinned Roslyn package
  exposes those syntax forms.
- A feature-scoped mutation result remains unavailable because repository Stryker
  globs compose with, rather than replace, the command-line mutate filter.

### Next session

1. Use `--refactorable-only --include-locations` to select a small consistency slice.
2. Add a deterministic mutation mapping only after an existing rt refactor operation
   can perform it safely.
3. Add C# 15 detectors when the compiler package supports the corresponding syntax.

### What was real and simulated

Steps 1–4 preserve the original protocol-design record and their sample values remain
simulated. Steps 5–6 are measured output from the implemented command and verification
gates on 2026-08-27.
