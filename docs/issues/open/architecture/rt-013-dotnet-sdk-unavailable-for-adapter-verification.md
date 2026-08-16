---
tags:
  - architecture
  - testing
ticket: rt-013
severity: High
status: Open
area: architecture
owner: ""
reported: 2026-08-16
summary: "The current execution environment has no dotnet SDK, preventing adapter-refactor verification"
---

# Install the pinned .NET SDK before adapter verification

## Context

The adapter-decoupling session cannot run `dotnet run`, build, format, or test:

```text
/bin/bash: line 1: dotnet: command not found
```

The refactor needs the SDK selected by `global.json` to validate project-reference
boundaries and execute CLI/MCP parity tests.

## Acceptance Criteria

- [ ] `dotnet --version` resolves an SDK compatible with `global.json`.
- [ ] The focused CLI/core and MCP test projects run locally.
- [ ] `scripts/check-local.sh` can run before the adapter-decoupling slice is handed off.
