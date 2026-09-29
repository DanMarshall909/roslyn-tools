---
tags:
  - architecture
  - testing
ticket: rt-013
severity: High
status: Resolved
area: architecture
owner: ""
reported: 2026-08-16
resolved: 2026-09-29
summary: "A prior execution environment lacked the .NET SDK and blocked adapter-refactor verification"
---

# Install the pinned .NET SDK before adapter verification

## Context

The 2026-08-16 adapter-decoupling session could not run `dotnet run`, build,
format, or test:

```text
/bin/bash: line 1: dotnet: command not found
```

The refactor needs the SDK selected by `global.json` to validate project-reference
boundaries and execute CLI/MCP parity tests.

## Acceptance Criteria

- [x] `dotnet --version` resolves an SDK compatible with `global.json`.
- [x] The focused CLI/core and MCP test projects run locally.
- [x] `scripts/check-local.sh` can run before the adapter-decoupling slice is handed off.

## Resolution

This environment now resolves .NET SDK `10.0.301`. The repository pins
`10.0.201` with `rollForward: latestFeature`, so the installed SDK is compatible.
The completed RT-020 extraction ran both owning test projects and the full local
gate. The gate was repeated on 2026-09-29 with
`bash scripts/check-local.sh --full`; its retained output is
`artifacts/rt-013-check-local-full.log`. The missing-SDK condition no longer
blocks adapter verification.

## Decision log

2026-09-29: Resolve the environment blocker after confirming the compatible
SDK and a passing full local build, both test projects, and format verification.
The shell script currently needs `bash` because its executable bit is unset;
that follow-up is recorded in RT-020 and does not block running the gate.
