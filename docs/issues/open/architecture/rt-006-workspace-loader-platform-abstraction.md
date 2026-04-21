---
tags:
  - task
ticket: rt-006
severity: High
status: Open
area: architecture
owner: ""
reported: 2026-04-21
summary: "WorkspaceLoader has 130 references across 30 commands — IWorkspaceSessionFactory must be a coordinated platform-wide refactor"
---

# Introduce `IWorkspaceSessionFactory` as a platform-wide refactor

| Field | Value |
|---|---|
| **Reported** | 2026-04-21 |
| **Target resolution** | |
| **Linked tickets** | rt-007, rt-005 |
| **Discovered in** | ralph/sessions/2026-04-21-change-signature-3.md step 4 |

## Description

`WorkspaceLoader.LoadAsync` is called directly in every one of the 30 command classes. `find-refs` returned 130 total references (production + test hooks).

Introducing `IWorkspaceSessionFactory` cannot be done safely feature-by-feature — any partial rollout leaves the codebase inconsistent. This must be a single coordinated change: introduce the interface, introduce a `WorkspaceSession` wrapper (since `WorkspaceLoader` is `static` and cannot implement an interface — see rt-005), update all 30 command handlers to accept the abstraction, and update test overrides accordingly.

## Blast radius (from `rt find-refs`)

- **30 production call sites** — every command in `src/RoslynTools/Commands/`
- **100+ test references** — `WorkspaceLoader.UseOverride(...)` in virtually every test fixture

## Proposed approach

1. Introduce `IWorkspaceSessionFactory` interface in `Core/`
2. Introduce `WorkspaceSession : IWorkspaceSessionFactory` — instance wrapper delegating to static `WorkspaceLoader`
3. Update all 30 commands to receive `IWorkspaceSessionFactory` via constructor or method parameter
4. Update test `UseOverride` pattern to stub `IWorkspaceSessionFactory` instead of the static
5. Keep `WorkspaceLoader` static internals unchanged; it becomes an implementation detail

Do this as a single PR — partial rollout is not safe.

## Acceptance Criteria

- [ ] `IWorkspaceSessionFactory` interface defined with `LoadAsync(string? solutionPath)` signature
- [ ] `WorkspaceSession` implements it, delegates to `WorkspaceLoader`
- [ ] All 30 command handlers accept `IWorkspaceSessionFactory`, not `WorkspaceLoader` directly
- [ ] No command file imports `WorkspaceLoader` after change
- [ ] All tests pass — test override mechanism updated to stub `IWorkspaceSessionFactory`
- [ ] `rt find-refs --symbol WorkspaceLoader --solution RoslynTools.slnx` shows 0 production call sites

## Open questions

- Should commands receive `IWorkspaceSessionFactory` via constructor (requires DI setup) or as a method parameter passed from `Build()`?
- Should `WorkspaceLoader.UseOverride` / `DiagnosticSinkOverride` AsyncLocals move to `WorkspaceSession`?

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-04-21 | Cannot be done per-feature | 30 callers; partial rollout creates inconsistency |
| 2026-04-21 | Needs `WorkspaceSession` wrapper | `WorkspaceLoader` is static, cannot implement interface (rt-005) |
