---
tags:
  - refactoring
  - code-smell
  - housekeeping
ticket: rt-010
severity: Low
status: Open
area: refactoring
owner: ""
reported: 2026-07-31
summary: "Backlog of complexity / long-method / structural-clone cleanups surfaced by the analysis suite — a snapshot, likely stale by implementation time"
---

# Analysis-suite cleanup backlog

## ⚠️ This is a point-in-time snapshot

Every item below was captured by running `rt`'s analysis tools against
`RoslynTools.slnx`, most recently refreshed on **2026-08-01**. Line numbers,
method sizes, and even the existence of these methods **will drift** as the
code changes. **Do not treat any line/size figure here as current.** Before
acting on any item, re-run the relevant tool and work from fresh output:

```powershell
rt analyze complexity   --sort cognitive --threshold 15 --solution "RoslynTools.slnx"
rt analyze list-smells  --solution "RoslynTools.slnx"
rt analyze find-duplicates --min-statements 6 --solution "RoslynTools.slnx"
```

If a re-run shows an item is already gone, tick it and note "resolved by drift".

## Snapshot findings (refreshed 2026-08-01)

### Complexity hotspots (cognitive) — unchanged since 2026-07-31
- `StaticClassInventoryService.AnalyzeAsync` — 83
- `FindDuplicatesService.RunAsync` — 80 (cyclomatic 40, ~150 LOC)
- `SolidAnalyzer.AnalyzeDipAsync` / `AnalyzeLspAsync` — 71 / 69
- `FindUnusedService.AnalyzeAsync` — 65
- `ReorderParamsService.ApplyReorderAsync` — 64

Analyzer/service code is inherently branchy; the goal is readability, not a target
number. Prefer extracting named sub-steps over chasing a metric.

### Structural smells — `list-smells` now reports 382 total (181 src, 201 tests);
only `LargeClass`/`LongMethod` trigger, no other category
- Largest classes in `src/`: `MethodExtractorDuplicates.cs` (1075 lines),
  `SolidAnalyzer.cs` (776), `RenameService.cs` (748), `SymbolLocator.cs` (647),
  `MethodExtractor.cs` (596)
- `App` — large class (553 lines); `RunCoreAsync` a 172-line method (cognitive
  complexity 38, not in the top-6 cognitive list above but still the largest
  method by LOC in `src/`)
- `DumpHelpCommand` — large class (393 lines)
- `ChangeSignatureCommand.Build` — 128-line method
- Assorted `Build` / `ExecuteAsync` methods in the 40–80 line range (155
  `LongMethod` hits total in `src/`, mostly this shape)

### Structural clones — `find-duplicates --min-statements 6` now reports 17
groups (8 after suppression); non-test groups:
- `Command`-building boilerplate repeated across `FindDuplicatesCommand`,
  `ImplementInterfaceCommand`, `InlineMethodCommand`, `SemanticDiffCommand`
  `Build()` methods (2 overlapping groups, score 5.82 and 3.88).
- Option-declaration blocks shared by `ReplaceCallCommand` / `ReplaceNewCommand`
  and `AddNullChecksCommand` / `InlineMethodCommand`.
- **New since 2026-07-31:**
  - `AddTestCommand.Build` duplicates a block against itself (two overlapping
    line ranges in the same method) — likely a copy-paste inside one method
    rather than a cross-file clone; worth a look on its own.
  - `MakeInstanceCommand.Build` / `MakeStaticCommand.Build` share a method-option
    block (`discovery_only` — contains a `return`, not mechanically extractable
    as-is).
  - `MethodExtractorDuplicates.UpgradeMethodVisibilityAsync` /
    `RegenerateForParameterizedVariationsAsync` share a path-resolution block
    (`discovery_only`, same reason).

(The 4× MCP handler test clone — Group 1, score 8.32, the highest-scoring group
in the whole report and the only one scoring "extractable" with no warnings —
is tracked separately in rt-009 and is still unaddressed.)

## Explicitly out of scope
- SOLID DIP counts (concrete ctor params, inline `new`) — dominated by Roslyn
  primitives; low signal, not worth chasing.
- Magic strings — almost entirely intentional CLI verbs/options
  (`refactor`, `inspect`, `--solution`, `--dry-run`); not a real problem.
- Static-class inventory — no migration is planned; recorded for reference only.

## Acceptance Criteria

- [ ] Re-run each tool and reconcile this list against current output before
      starting.
- [ ] Address complexity/smell items opportunistically when touching the file for
      other reasons, rather than as a dedicated big-bang pass.
- [ ] Extract the genuinely-shared command-building boilerplate where it reduces
      duplication without over-abstracting one-off cases.

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-07-31 | File as one low-priority backlog rather than per-item tickets | These are opportunistic cleanups; individual tickets would go stale faster than they'd be actioned. |
| 2026-08-01 | Refreshed against current `complexity`/`list-smells`/`find-duplicates` output | Complexity hotspots and the previously-listed clone groups are unchanged — this backlog hasn't gone stale yet. Added 3 new clone groups and current `list-smells` totals (382, was previously unquantified). |
