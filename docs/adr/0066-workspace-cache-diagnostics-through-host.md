# ADR-0066: Workspace cache diagnostics flow through host composition

- Status: Accepted
- Date: 2026-09-29
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0060](0060-service-diagnostics-without-ambient-console.md)

## Context

Core's workspace cache telemetry called the ambient `RtLog` bridge for every
cache event and for trace-write failures. This made a reusable cache component
choose process logging even when used in an MCP, daemon, or test host.

## Decision

Give `WorkspaceCacheTelemetry` callbacks for cache events and trace failures.
Shared host composition supplies callbacks backed by its injected logger with
the same messages and levels. The cache still owns its optional JSONL trace
file and keeps the same best-effort failure handling. A source test rejects
ambient `RtLog` calls in the workspace cache source, and a behavior test
checks event reporting alongside trace serialization.

## Consequences

- Host composition chooses the diagnostic destination; Core cache telemetry
  does not reach into process-global logging state.
- Test-only telemetry construction can omit a diagnostic callback and remain
  silent while exercising trace behavior.
- Other Core helpers still use ambient console and context state; #53 remains
  open for those tracers.
