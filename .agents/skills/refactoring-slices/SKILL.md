---
name: refactoring-slices
description: Deliver a broad refactor as small, independently verifiable, behavior-preserving slices. Use when planning or implementing architecture migrations, repeated command conversions, structural cleanup, or parallel refactoring work; use the general refactor skill for a read-only quality sweep.
---

# Refactoring Slices

Turn a large refactor into a sequence of narrow changes that can be reviewed, tested, and landed independently. Preserve observable behavior unless the user explicitly asks for a behavior change.

## Establish the slice contract

Before editing, state:

- the single seam or invariant this slice changes;
- the files or layer boundary it owns;
- the behavior that must remain stable;
- the narrow test that proves the slice;
- shared registries, documentation, or generated artifacts that need coordinator ownership.

Inspect the current branch, working tree, project instructions, and any live specification or tracking issue. Preserve unrelated changes. Do not commit, push, file issues, or mutate external systems unless the user has authorized that action.

## Choose the slice type

### Mechanical symbol change

Examples: rename, move, make sealed, or delete a proven-unused symbol.

1. Resolve the symbol semantically and inspect its references.
2. Run the relevant `rt refactor` command with `--dry-run`.
3. Review the complete diff, then apply only the confirmed operation.
4. Build and run the narrow owning tests.

### Behavior-preserving structural change

Examples: extract a method, split a class, consolidate duplication, or replace an internal representation.

1. Add or identify characterization tests for the observable boundary.
2. Confirm those tests pass before the edit.
3. Move one responsibility or abstraction boundary at a time.
4. Re-run the characterization test after each coherent edit.
5. Remove superseded code only after all callers use the new seam.

Snapshot tests are optional, not a default dependency. Prefer ordinary assertions when the behavior is compact and stable.

### Host or architecture decoupling

Examples: move a CLI command behind an application port or make CLI and MCP share one operation.

Read [references/command-migration-groups.md](references/command-migration-groups.md) and follow the matching command group. Treat the operation contract, host adapters, registration, RPC serialization, error mapping, cancellation, and architecture tests as one vertical slice.

### Logic-changing cleanup

This is a behavior change, not a pure refactor. Use the repository's TDD workflow: demonstrate the missing or wrong behavior with a failing test, implement the minimum fix, then refactor under green tests.

## Scale out safely

Prove one representative slice from each category before fanning out the remainder. The pilot establishes naming, request/result shape, error policy, adapter shape, and verification commands.

For parallel work:

- assign each worker one category or disjoint file set;
- give each worker the pilot as the pattern and an explicit stop condition;
- reserve shared catalogues, dependency registration, generated docs, and architecture allowlists for one coordinator;
- require workers to report files changed, tests run, unresolved decisions, and any shared-file edits they avoided;
- integrate and verify one worker result at a time so the first regression has a small search space.

Do not fan out ambiguous design decisions. Resolve the pattern in the pilot first.

## Evidence gates

Use the smallest meaningful gate at each stage:

```text
baseline test -> slice-specific test -> owning project -> solution gate
```

For a mutation, inspect a dry-run diff before applying it. For a serialization or host-boundary change, test both success and typed failure through the real adapter or dispatcher. For a layer change, run the architecture tests that enforce dependency direction.

Before handoff, run the repository's documented local gate when practical. If the full gate is blocked, report the exact blocker and distinguish it from the checks that passed.

## Completion

A slice is done when:

- its old and new boundaries are not both active accidentally;
- behavior and error semantics are covered;
- cancellation and working-directory behavior are preserved where relevant;
- no host-specific dependencies leaked into the operation or service layer;
- task-relevant documentation and tracking state match the implementation;
- the working tree contains no unexplained files from the slice.

Report the outcome, verification evidence, and the next independent slice. Keep commits logically scoped; do not mix unrelated cleanup into the slice.
