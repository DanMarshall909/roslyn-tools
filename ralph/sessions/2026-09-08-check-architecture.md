# Session: 2026-09-08 — Executable Architecture Boundary Verification

**Goal:** Add one typed, read-only CLI/MCP operation that validates checked-in architectural dependency rules and emits CI-suitable evidence.
**Solution:** RoslynTools.slnx

---

## Step 1 — check-architecture [SIMULATED]

`rt analyze check-architecture --solution WageClaims.sln --config .rt/architecture.json --json --trust-repository`

<!-- SIMULATED — command does not exist yet -->

```json
{
  "isCompliant": false,
  "violationCount": 1,
  "newViolationCount": 1,
  "violations": [
    {
      "rule": "layer-project-reference",
      "layer": "Infrastructure",
      "project": "WageClaims.Infrastructure",
      "file": "src/backend/Infrastructure/WageClaims.Infrastructure.csproj",
      "line": 12,
      "dependency": "WageClaims.Functions",
      "source": "<ProjectReference Include=\"..\\Functions\\WageClaims.Functions.csproj\" />",
      "baselineState": "new"
    }
  ]
}
```

> **[ISSUE: rt-017]** written — RT has no executable, descriptor-backed architecture boundary verifier.

---

## Retrospective

### Real commands used

- `rg`

### Simulated commands → rt candidates

- `check-architecture` — high-value solution-wide analysis with a checked-in JSON policy.

### Gaps logged

- 2026-09-08: rt-017 — no typed CLI/MCP operation validates project, semantic, SDK, framework, package, and technology boundaries.

### Next session

- Implement the typed request/result and config validation, then prove CLI/MCP parity and trusted/untrusted behavior.

## Follow-up outcome

Implemented the simulated protocol as the typed `check-architecture` CLI/MCP
operation. The real command verifies this repository's five configured projects with
zero violations. Focused tests cover deterministic findings, visible baselines,
contained paths, malformed configuration, missing configured projects, incomplete
compilation, CLI exit semantics, schema parity, and trusted/untrusted MCP behavior.

The complete local gate passed on 2026-09-08: 1,841 core/CLI tests and 107 MCP tests
passed, with 9 intentional skips; build and formatting verification were clean.
