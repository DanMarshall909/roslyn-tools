---
tags:
  - bug
ticket: rt-002
severity: High
status: Open
area: workspace-loader
owner: ""
reported: 2026-04-21
summary: "WorkspaceLoader rejects .slnx files, blocking rt from analysing its own solution"
---

# WorkspaceLoader rejects .slnx format

| Field | Value |
|---|---|
| **Reported** | 2026-04-21 |
| **Target resolution** | |
| **Linked tickets** | |
| **Discovered in** | ralph/sessions/2026-04-21-change-signature.md step 2 |

## Description

`WorkspaceLoader.ValidateSolutionFormat` throws unconditionally when passed a `.slnx` path:

```csharp
if (solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException(
        $"{sourceDescription} '{solutionPath}' uses unsupported format '.slnx'. Pass a .sln file instead.");
```

`RoslynTools.slnx` is the solution format for this repo, so rt cannot dogfood against itself. Every read-only command (`outline`, `find-refs`, `inspect`, etc.) is blocked when `--solution RoslynTools.slnx` is passed. CLAUDE.md and the smoke-test matrix both reference `RoslynTools.slnx`, misleading users and agents.

## Steps to reproduce

```
rt outline src/RoslynTools/Commands/ChangeSignatureCommand.cs --solution RoslynTools.slnx
```

```
Unhandled exception: System.InvalidOperationException: Explicit solution path '...\RoslynTools.slnx'
uses unsupported format '.slnx'. Pass a .sln file instead.
```

## Impact

- rt cannot self-dogfood: every smoke test in CLAUDE.md that uses `RoslynTools.slnx` fails
- Session log sessions are forced to simulate all commands instead of running real ones
- Any user or agent following CLAUDE.md instructions hits this immediately

## Proposed fix

Remove `ValidateSolutionFormat` (or soften to a warning) and probe whether `MSBuildWorkspace.OpenSolutionAsync` handles `.slnx` natively in Roslyn 4.9.2. If it does, delete the guard. If it fails, catch the inner exception and surface a clear error pointing to the `.sln` equivalent.

Separately, update CLAUDE.md smoke-test matrix to use a path that actually works until this is resolved.

## Acceptance Criteria

- [ ] `rt outline src/RoslynTools/Commands/ChangeSignatureCommand.cs --solution RoslynTools.slnx` exits 0
- [ ] `rt find-refs --symbol WorkspaceLoader --solution RoslynTools.slnx` exits 0
- [ ] If MSBuildWorkspace does not support `.slnx`, error message names the limitation clearly (not a generic exception)
- [ ] CLAUDE.md smoke-test matrix references a path that works

## Open questions

- Does `MSBuildWorkspace.OpenSolutionAsync` in Roslyn 4.9.2 support `.slnx`? Unknown until probed.
- If not: should we ship a `.sln` wrapper file in the repo for dogfooding purposes?

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-04-21 | Guard added previously | MSBuildWorkspace support for .slnx unconfirmed at time of writing |
