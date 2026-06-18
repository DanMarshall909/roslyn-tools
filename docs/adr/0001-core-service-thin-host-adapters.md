# ADR-0001: Core service + thin host adapters

- Status: Accepted
- Date: 2026-06-17
- Stories: pre-EP-013 doctrine; reified by `suggest` (greenfield) and `outline` (migration) tracers; later epics (EP-008 workspace boundary, EP-009 command surface, EP-010 error handling, EP-012 thin-host migration) all build on this.

## Context

`rt` started as a Windows CLI. Adding MCP support to the same logic surfaced two anti-patterns common in dual-host codebases:

1. **Thick CLI**: business logic lives inside `*Command.cs`. The second host either re-implements or screen-scrapes the CLI's text. Brittle.
2. **Leaky service**: a "shared" service references `System.CommandLine`, `Console.WriteLine`, or specific flag shapes. Can't be used by anything that isn't the CLI.

Both make the second host a second-class citizen. We needed a pattern that treats CLI and MCP as equal-status consumers of a single canonical surface.

## Decision

Every feature follows the **Core service + thin host adapters** pattern:

- **Service layer** (`src/RoslynTools/Services/`) contains the logic. Pure: no `System.CommandLine`, no `Console`, no IO except via injected dependencies. Receives domain request objects; returns `Result<T, RtError>` or `RtResult<T>`.
- **CLI adapter** (`src/RoslynTools.Cli/Commands/<Foo>Command.cs`) parses args, calls the service via DI, renders text or JSON via `RtResultRenderer.Write<T>`.
- **MCP adapter** (`src/RoslynTools.Mcp/Handlers/<Foo>Handler.cs`) parses JSON args, calls the same service via DI, returns `ToolResult`.

Both hosts resolve the service from the same DI container; neither knows about the other.

**Test of belonging:** when adding code, ask — "Could I unit-test this without any host references in scope?" If no, it belongs in a different layer.

Worked examples and the full layer description live at [pattern-core-service.md](../pattern-core-service.md).

## Consequences

**Easier:**

- A new host (web API, batch worker) can be added without re-implementing logic.
- Tests run against the service directly — no host harness needed.
- CLI/MCP parity is structural, not policed — both literally call the same code.
- Architecture tests can ratchet contracts (no `Console.Write*` outside CLI adapter; no `System.CommandLine` types in services).

**Harder:**

- Two adapter files per feature instead of one CLI command.
- DTOs need to be designed for both transports (JSON shape must serialize cleanly; argument shape must parse cleanly).
- Migration of pre-existing thick-CLI commands is slow — tracked under EP-012.

**Invariants enforced:**

- `NoDirectConsoleUse` architecture test forbids `Console.Write*` outside the CLI adapter layer.
- `NoConcreteWorkspaceLoaderInProduction` + `NoStaticWorkspaceLoaderUse` keep workspace IO injected.
- `NoRtServiceProviderInProduction` (US-072) eliminated the service-locator anti-pattern that let services reach across layers.
- CLI/MCP parity is verifiable by running both adapters against the same fixture and diffing output (used in US-100 byte-identity gate).

**Deliberately deferred:**

- Full thick-CLI migration of all pre-pattern commands (EP-012, multiple stories).
- Capability-flag interfaces backed by a central catalog (US-114, EP-009) — services are flag-agnostic today by convention; US-114 enforces it.

## Alternatives considered

- **Thick CLI + screen-scrape from MCP**: rejected — brittle, every CLI cosmetic change breaks MCP.
- **Re-implement logic per host**: rejected — every bug needs to be fixed twice.
- **One host wraps the other (MCP shells out to CLI)**: rejected — startup cost per call, error surface is text-only.

## References

- Doc: [../pattern-core-service.md](../pattern-core-service.md)
- Doc: [../target-command-surface-architecture.md](../target-command-surface-architecture.md)
- Doc: [../architecture-overview.md](../architecture-overview.md)
- Code: every command under `src/RoslynTools.Cli/Commands/` (CLI adapter), every handler under `src/RoslynTools.Mcp/Handlers/` (MCP adapter), every service under `src/RoslynTools/Services/`.
