---
tags:
  - task
ticket: rt-001
severity: Medium
status: Closed
area: commands
owner: ""
reported: 2026-04-17
closed: 2026-05-15
summary: "Add inject-dependency and replace-call commands to handle bulk refactors that rt currently can't do"
resolution: "Shipped as rt refactor add-dependency (inject-dependency), rt refactor replace-call (replace-call), and rt refactor convert-console-logging (original Console→logger motivation)."
---

# Add `inject-dependency` and `replace-call` Roslyn commands

| Field | Value |
|---|---|
| **Reported** | 2026-04-17 |
| **Target resolution** | |
| **Linked tickets** | rt-001 |

## Description

Two new commands needed to support bulk refactors that `rt` currently has no answer for.

Motivated by needing to replace 147 `Console.Error.WriteLine` / `Console.WriteLine` calls with `_logger.LogError` / `_logger.LogInformation` across the `AdminController` partial class family in tour-store — a task that requires both injecting `ILogger<T>` into the constructor-bearing file and then replacing all call sites.

### 1. `inject-dependency`

Adds a constructor parameter, a private readonly backing field, and the assignment statement to a named class. Must handle partial classes correctly (find the file that contains the constructor, not just any partial file).

```
rt inject-dependency --class AdminController --type "ILogger<AdminController>" --field _logger --solution tour-store.sln
```

Behaviour:
- Locates the class declaration that has the constructor
- Adds `private readonly ILogger<AdminController> _logger;` field
- Adds `ILogger<AdminController> logger` parameter to constructor
- Adds `_logger = logger;` assignment in constructor body
- Adds necessary `using Microsoft.Extensions.Logging;` if missing
- Supports `--dry-run`

### 2. `replace-call`

Replaces all invocations of one static method with another across the solution, with configurable argument mapping.

```
rt replace-call \
  --from "Console.Error.WriteLine" \
  --to "_logger.LogError" \
  --arg-map "ex,{0}" \
  --solution tour-store.sln
```

Behaviour:
- Matches all `Console.Error.WriteLine(...)` call expressions via Roslyn AST (not regex)
- Replaces with `_logger.LogError(ex, <message>)` using configurable arg mapping
- Handles string interpolation → structured logging parameter conversion (nice-to-have)
- Supports `--dry-run`
- Scopes to a specific file, namespace, or type via optional `--in` filter

## Steps to reproduce

1. In any project with many `Console.Error.WriteLine` calls in controller catch blocks
2. Try `rt` — no command handles this
3. Manual file-by-file edits required instead

## Impact

Blocks efficient migration from `Console.*` to structured logging in large codebases. Currently requires 147 manual edits across 8 files in tour-store alone.

## Proposed fix

Follow the `ReplaceTypeCommand` (replace-datetime) pattern:
- Phase 1: `CSharpSyntaxRewriter` to find and replace call sites
- Phase 2: Constructor injector rewriter (reuse/adapt from `ReplaceTypeCommand`)
- Wire up in `App.cs` and add aliases (`inject-dep`, `replace-call`)

See existing `ReplaceTypeCommand.cs` — the constructor injection half is already implemented there and can be extracted/generalised.

## Acceptance Criteria

- [ ] `rt inject-dependency` adds field + ctor param + assignment to the constructor-bearing partial file
- [ ] `rt replace-call` replaces matching invocations across all project files
- [ ] Both support `--dry-run` with unified diff output
- [ ] Both support `--solution` path override
- [ ] `inject-dependency` adds missing `using` directives
- [ ] Integration test covering a partial class with multiple files

## Open questions

- How should `replace-call` handle argument mapping for complex interpolated strings?
- Should `replace-call` support a config file for batch replacements (multiple from→to pairs)?

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-04-17 | Log as issue rather than implement immediately | Unblocked by manual edits; rt improvement is a backlog item |
