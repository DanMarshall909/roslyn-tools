---
title: Refactor safely
description: Gather semantic evidence, preview a refactoring, and apply reviewed changes across your C# solution with Roslyn Tools.
order: 6
section: Refactoring
---

Gather semantic evidence through the CLI or MCP before changing code. An MCP host with startup repository approval exposes eligible refactoring tools with dry-run defaults. The workflow below uses the CLI: establish the workspace, select the symbol, preview the operation, then verify the repository diff.

## Preview before applying

Use `--dry-run` to inspect the intended source changes:

```powershell
rt refactor rename BookingService ReservationService \
  --solution MyApp.slnx --dry-run
```

For automation, request a structured format so the plan can be inspected without parsing rendered text:

```powershell
rt refactor move BookingService --to src/Reservations \
  --solution MyApp.slnx --dry-run --json
```

After applying a change, review `git diff` and run the owning build and tests.

## Make ambiguity repeatable

Commands that need a user choice can read and write a decisions file:

```powershell
rt refactor rename OldName NewName \
  --solution MyApp.slnx --decisions .rt-decisions.yml
```

When choices remain unresolved, `rt` records the candidates and exits with code `2`. Fill in the `choose:` values and rerun the same command. This turns an interactive choice into a reviewable input.

## Understand confidence limits

Closed-world claims require a complete workspace and evidence that relevant consumers are visible. Reflection, dependency injection, serialization, markup, generators, and external consumers can make apparently unused code reachable.

`delete-unreferenced` therefore prepares a deletion candidate rather than promising universal safety. Keep public and externally visible symbols unless the declared completeness and confidence policy supports the conclusion.

## Use narrow operations

Prefer the operation that expresses the intended change—`rename`, `move`, `extract-method`, `make-async`, or another focused refactoring—over a broad mechanical rewrite. Narrow commands produce clearer plans, smaller diffs, and more meaningful verification.
