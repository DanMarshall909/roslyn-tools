---
tags:
  - task
ticket: rt-007
severity: High
status: Open
area: architecture
owner: ""
reported: 2026-04-21
summary: "SolutionWriter has 22 fan-in from commands — ISolutionApplier abstraction needed alongside IWorkspaceSessionFactory"
---

# Introduce `ISolutionApplier` abstraction for `SolutionWriter`

| Field | Value |
|---|---|
| **Reported** | 2026-04-21 |
| **Target resolution** | |
| **Linked tickets** | rt-006 |
| **Discovered in** | ralph/sessions/2026-04-21-change-signature-3.md step 7 |

## Description

`SolutionWriter.ApplyChangesAsync` is called directly in every mutation command — `dependency-graph` output shows 22 fan-in from the `Commands` scope. It has the same direct-coupling problem as `WorkspaceLoader` (rt-006).

`ISolutionApplier` should be introduced alongside `IWorkspaceSessionFactory` in the same platform-wide PR (rt-006). Splitting them into separate PRs would mean going through the full 30-command update twice.

## Current shape (from source)

```csharp
// Called in every mutation command:
await SolutionWriter.ApplyChangesAsync(loadedSolution, updatedSolution, dryRun);
```

## Proposed interface

```csharp
internal interface ISolutionApplier
{
    Task ApplyAsync(Solution original, Solution updated, bool dryRun);
}
```

`SolutionWriter` implements it. All mutation commands receive `ISolutionApplier` alongside `IWorkspaceSessionFactory`.

## Acceptance Criteria

- [ ] `ISolutionApplier` interface defined
- [ ] `SolutionWriter` implements it
- [ ] All mutation commands accept `ISolutionApplier`, not `SolutionWriter` directly
- [ ] `rt find-refs --symbol SolutionWriter --solution RoslynTools.slnx` shows 0 production call sites after change
- [ ] Implemented in the same PR as rt-006

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-04-21 | Bundle with rt-006 | Same blast radius; doing separately doubles churn |
