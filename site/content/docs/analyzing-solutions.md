---
title: Analyze solutions
description: Measure dependencies, complexity, language use, and likely defects across a complete Roslyn workspace.
order: 4
section: Analysis
---

Analysis commands look across a solution rather than answering a single-symbol question. Their results are designed for review, baselining, and automation.

## Start with workspace health

Solution-wide conclusions depend on what Roslyn loaded. Run `doctor` first when SDKs, target frameworks, references, generators, or analyzers may be unavailable:

```powershell
rt inspect doctor --solution MyApp.slnx --json
```

A partial workspace can still produce useful evidence, but it must not be treated as proof that a symbol is unused or safe to delete.

## Measure structure

Use the dependency and complexity commands to find areas that deserve attention:

```powershell
rt analyze dependency-graph --solution MyApp.slnx --json
rt analyze circular-deps --solution MyApp.slnx --json
rt analyze complexity --solution MyApp.slnx --json
```

The commands keep primary results on stdout and diagnostics on stderr, making structured output suitable for pipelines.

## Find focused signals

The analysis surface includes deliberately narrower inventories and heuristics:

```powershell
rt analyze find-duplicates --solution MyApp.slnx
rt analyze find-magic-strings --solution MyApp.slnx
rt analyze list-smells --solution MyApp.slnx
rt analyze language-features --solution MyApp.slnx
```

Heuristic findings are prompts for investigation, not compiler errors. Preserve their evidence and limitations when reporting them.

## Compare semantic changes

`semantic-diff` compares C# structure while ignoring formatting-only noise:

```powershell
rt analyze semantic-diff --from HEAD~1 --to HEAD --solution MyApp.slnx --json
```

Use it alongside Git rather than as a replacement for the textual diff. The semantic view explains what declarations changed; Git remains the source of the exact patch.
