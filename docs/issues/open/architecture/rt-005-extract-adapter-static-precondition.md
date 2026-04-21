---
tags:
  - bug
ticket: rt-005
severity: Medium
status: Open
area: architecture
owner: ""
reported: 2026-04-21
summary: "rt extract-adapter must detect static types and refuse or offer a wrapper — static classes cannot implement interfaces"
---

# `rt extract-adapter` fails silently on static types

| Field | Value |
|---|---|
| **Reported** | 2026-04-21 |
| **Target resolution** | |
| **Linked tickets** | rt-003 |
| **Discovered in** | ralph/sessions/2026-04-21-change-signature-2.md step 6 |

## Description

`rt extract-adapter` (once implemented) will extract an interface from a concrete type and make the type implement it. This fails on static classes: C# does not allow static classes to implement interfaces.

`WorkspaceLoader` is `static`, so `rt extract-adapter --type WorkspaceLoader --interface IWorkspaceSessionFactory` would either:
- produce uncompilable output (adds `: IWorkspaceSessionFactory` to a `static class`)
- crash with an unguarded Roslyn exception

The tool must detect this pre-condition and either refuse with a clear message, or offer to introduce an instance wrapper class (`WorkspaceSession`) that delegates to the static implementation.

## Steps to reproduce (once extract-adapter exists)

```
rt extract-adapter \
  --type RoslynTools.Core.WorkspaceLoader \
  --interface IWorkspaceSessionFactory \
  --solution RoslynTools.slnx
```

Expected: clear error or prompt to introduce wrapper.
Actual (without guard): uncompilable diff or crash.

## Proposed fix

In `extract-adapter` pre-validation, check `typeSymbol.IsStatic`. If true:

**Option A — Refuse:** emit an error:
```
Error: 'WorkspaceLoader' is a static class and cannot implement an interface.
Introduce an instance wrapper first: rt introduce-wrapper --type WorkspaceLoader --name WorkspaceSession
```

**Option B — Auto-wrap:** offer to generate a `WorkspaceSession` adapter class that holds no state and delegates every interface method to the static `WorkspaceLoader`. Emit both files and the interface.

Option A is safer for the initial implementation.

## Acceptance Criteria

- [ ] `rt extract-adapter` on a static type exits non-zero with a descriptive error
- [ ] Error message names the type, explains the constraint, and suggests a remediation path
- [ ] No uncompilable output is written to disk
- [ ] `--dry-run` also respects the pre-condition check

## Open questions

- Should `rt introduce-wrapper` be a standalone command (Option A path), or folded into `extract-adapter --wrap-static`?

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-04-21 | Discovered during session log simulation | Static constraint not visible until extract-adapter was designed end-to-end |
