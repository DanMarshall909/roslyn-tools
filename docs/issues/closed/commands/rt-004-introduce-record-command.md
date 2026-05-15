---
tags:
  - task
ticket: rt-004
severity: Medium
status: Closed
area: commands
owner: ""
reported: 2026-04-21
closed: 2026-05-15
summary: "Add rt introduce-record command to extract a record type from method parameter lists"
resolution: "Shipped as rt refactor transform-parameters (tp) — extracts method parameters into a new record and updates all call sites."
---

# Add `rt introduce-record` command

| Field | Value |
|---|---|
| **Reported** | 2026-04-21 |
| **Target resolution** | |
| **Linked tickets** | rt-003 |
| **Discovered in** | ralph/sessions/2026-04-21-change-signature-2.md step 4 |

## Description

Before `extract-orchestrator` can use a typed request object, the request record must exist. `introduce-record` creates it from the parameter names and types on a given method or lambda, so the two commands compose naturally.

Also surfaces a cross-cutting design decision: `Solution` and `DryRun` appear on every command's handler. A shared `RtRequest` base record should be introduced once and reused — this command is the right place to enforce that convention.

## Proposed CLI shape

```
rt introduce-record \
  --name <RecordName> \
  --from-method "<FQN.MethodName>" \
  --params "<param1,param2,...>" \
  --namespace <TargetNamespace> \
  --solution <path> \
  [--dry-run]
```

## Reference output (from session log)

See `ralph/sessions/2026-04-21-change-signature-2.md step 4`. Expected output:

```csharp
namespace RoslynTools.Features.ChangeSignature;

internal sealed record RenameParameterRequest(
    string MethodSpec,
    string OldName,
    string NewName,
    string? Solution,
    bool DryRun);
```

## Acceptance Criteria

- [ ] Emits a `sealed record` with correct property names (PascalCase from camelCase params) and types inferred from the source method
- [ ] Places file in the target namespace folder
- [ ] `--dry-run` shows the new file content
- [ ] Handles nullable reference types (`string?`, `bool?`)
- [ ] Does not duplicate `Solution`/`DryRun` if a base `RtRequest` record is already in scope (future-proofing hook)

## Open questions

- Should `--params` be optional, defaulting to all parameters of `--from-method`?
- When should `RtRequest` base record be introduced, and should this command reference it?

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-04-21 | Defer RtRequest base record | Don't over-engineer before the pattern proves out across 2+ commands |
