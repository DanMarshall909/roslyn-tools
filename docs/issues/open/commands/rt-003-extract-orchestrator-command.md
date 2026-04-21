---
tags:
  - task
ticket: rt-003
severity: High
status: Open
area: commands
owner: ""
reported: 2026-04-21
summary: "Add rt extract-orchestrator command to pull handler logic out of fat command classes"
---

# Add `rt extract-orchestrator` command

| Field | Value |
|---|---|
| **Reported** | 2026-04-21 |
| **Target resolution** | |
| **Linked tickets** | rt-004 |
| **Discovered in** | ralph/sessions/2026-04-21-change-signature.md step 4, ralph/sessions/2026-04-21-change-signature-2.md step 5 |

## Description

Every command in rt mixes CLI binding, workspace lifecycle, orchestration, and error handling in a single `SetHandler` lambda. `extract-orchestrator` would pull the handler body into a dedicated `<Feature>Handler` class, leaving the command as a thin CLI adapter.

The exact diff shape was designed across two session log sessions. The acceptance test is the simulated diff in `ralph/sessions/2026-04-21-change-signature-2.md step 5`.

## Proposed CLI shape

```
rt extract-orchestrator \
  --type <CommandType> \
  --name <HandlerName> \
  --request <RequestRecordType> \
  --namespace <TargetNamespace> \
  --solution <path> \
  [--dry-run]
```

## What it does

1. Identifies the `SetHandler` lambda body on the target command type
2. Creates `<HandlerName>.cs` in the target namespace with a `static async Task<int> HandleAsync(<RequestType> request)` method containing the extracted body
3. Replaces the lambda body with a single `context.ExitCode = await <HandlerName>.HandleAsync(new <RequestType>(...))` call
4. Updates `using` directives in the command file

## Reference output (from session log)

See `ralph/sessions/2026-04-21-change-signature-2.md step 5` for the exact expected diff against `ChangeSignatureCommand.cs`. Use this as the integration test fixture.

## Acceptance Criteria

- [ ] `rt extract-orchestrator --type RoslynTools.Commands.ChangeSignatureCommand --name RenameParameterHandler --request RenameParameterRequest --namespace RoslynTools.Features.ChangeSignature --solution RoslynTools.slnx --dry-run` produces the diff in session-2 step 5
- [ ] `ChangeSignatureCommand.Build()` drops from 76 to ~42 lines after apply
- [ ] Command file no longer imports `WorkspaceLoader` or `SolutionWriter`
- [ ] Test suite passes after apply (no broken imports)
- [ ] `--dry-run` shows diff; without flag, writes files

## Open questions

- Should `--request` be required, or should the tool optionally generate flat parameters as a fallback (session-1 shape)?

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-04-21 | Use request record, not flat params | Flat params spread to 30 commands; record is cleaner and enables rt-005 |
