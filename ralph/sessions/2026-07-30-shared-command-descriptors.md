# Session: 2026-07-30 — Shared Command Descriptors

**Goal:** Execute FB-002 by proving a shared command-descriptor and application-port path for CLI and MCP.
**Solution:** RoslynTools.slnx

---

## Step 1 — inspect current MCP command surface [REAL]

`rt inspect outline src/RoslynTools.Mcp --solution RoslynTools.slnx --summary`

The current surface contains `CliCommandBridge`, `ToolSchemaBuilder`, `ToolRegistry`,
and eight `IMcpHandler` implementations. `OutlineHandler` repeats target dispatch
already present in `OutlineCommand`.

> **[ISSUE: rt-007]** written — CLI and MCP command surfaces lack one shared descriptor and invocation port.

---

## Step 2 — inspect duplicated outline dispatch [REAL]

`rt inspect source RoslynTools.Commands.OutlineCommand --solution RoslynTools.slnx`

`rt inspect source RoslynTools.Mcp.Handlers.OutlineHandler --solution RoslynTools.slnx`

Both adapters select among regex, namespace, glob, directory, and file operations
independently. The shared `OutlineRequest` exists, but there is no single operation
that owns dispatch and validation.

---

## Step 3 — shared descriptor projection [SIMULATED]

`rt dump-help --json`

```json
{
  "path": ["inspect", "outline"],
  "toolName": "outline",
  "mcpVisible": true,
  "safety": "readOnly",
  "structured": true,
  "requestType": "RoslynTools.Services.OutlineRequest"
}
```

The descriptor supplies command identity, metadata, CLI builder, MCP visibility,
safety policy, schema source, and optional typed application port.

---

## Step 4 — generic MCP invocation [SIMULATED]

`mcp tools/call outline { "target": "src/RoslynTools/Core", "solution": "RoslynTools.slnx" }`

The generic MCP adapter binds JSON to `OutlineRequest`, invokes `OutlineOperation`,
and returns its structured result. `OutlineCommand` constructs the same request and
invokes the same operation. No `OutlineHandler` remains.

---

## Retrospective

### Real commands used

- `outline`
- `source`
- `feedback list`

### Simulated commands → rt candidates

- None. The simulation describes internal host architecture, not a new user command.

### Gaps logged

- 2026-07-30: rt-007 — shared command descriptors and application ports.

### Next session

- Extend the port-backed descriptor migration from the tracer commands to the remaining legacy bridge commands.

