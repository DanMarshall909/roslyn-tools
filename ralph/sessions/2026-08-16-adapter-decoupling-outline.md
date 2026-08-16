# Session: 2026-08-16 — Adapter Decoupling: Outline Slice

**Goal:** Establish `inspect outline` as the first complete CLI/MCP-to-daemon typed-operation slice, without introducing an IDE adapter.
**Solution:** RoslynTools.slnx

---

## Step 1 — inspect outline [REAL - BLOCKED]

`dotnet run --project src/RoslynTools.Cli/RoslynTools.Cli.csproj --no-launch-profile -- inspect outline src/RoslynTools/Services/OutlineOperation.cs --solution RoslynTools.slnx --summary --json`

```text
/bin/bash: line 1: dotnet: command not found
```

> **[ISSUE: rt-013]** written — this environment has no .NET SDK, so the required build and runtime verification cannot run here.

---

## Step 2 — map the existing typed outline operation [REAL]

`rg "OutlineOperation|OutlineRequest|CliCommandBridge|DaemonRpc" src tests`

`OutlineOperation` already owns target validation and workspace dispatch, but its descriptor and local-RPC protocol are currently owned by the CLI assembly. MCP references that assembly and retains `CliCommandBridge` for legacy commands.

---

## Step 3 — daemon-owned outline result [SIMULATED]

`rt inspect outline RoslynTools.Services --solution RoslynTools.slnx --json`

```json
{
  "data": [{ "fqn": "RoslynTools.Services.OutlineOperation" }],
  "suggestedTools": [
    { "toolName": "describe", "whenToUse": "Use after outline when a returned type or member needs a focused signature view." },
    { "toolName": "find_refs", "whenToUse": "Use after outline when you need to trace references to a symbol it identified." }
  ]
}
```

The CLI binds `OutlineRequest` and renders this response; MCP binds the same request
JSON and renders the same daemon result. Neither adapter creates the suggestions.

### Next session

- Start the daemon before exercising this slice; the migrated command intentionally no longer falls back to argv/stdout forwarding.

---

## Retrospective

### Real commands used

- `rg`

### Simulated commands → rt candidates

- None.

### Gaps logged

- 2026-08-16: rt-013 — no .NET SDK is available for build, test, or live CLI verification.

### Next session

- Build and run the focused adapter-contract tests with the SDK pinned by `global.json`.
