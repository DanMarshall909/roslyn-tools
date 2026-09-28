# ADR-0061: Console logger belongs to host composition

- Status: Accepted
- Date: 2026-09-28
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0060](0060-service-diagnostics-without-ambient-console.md)

## Context

Services no longer call ambient console diagnostics directly, but ten
parameterless service constructors still chose `RtConsoleLogger<T>`. This made
standalone service construction implicitly select a host output sink. The
concrete logger implementation also remained compiled into Core, even though
shared Hosting registers it for CLI, MCP, and daemon composition.

## Decision

Move `RtConsoleLogger<T>` unchanged to shared Hosting. Keep the host DI
registration as the place that selects the console adapter. Parameterless
service constructors use `NullLogger<T>.Instance` for standalone/test use;
callers that need diagnostics pass an `ILogger<T>`. Extend the architecture
ratchet to require the concrete logger in Hosting and reject its construction
inside Services.

## Consequences

- Host-composed CLI/MCP/daemon logging remains unchanged.
- Direct parameterless construction of a service is silent unless a logger is
  supplied. The typed application paths use host DI and remain diagnostic.
- The direct move result-rendering follow-up is completed by
  [ADR-0062](0062-legacy-move-preview-result.md). Other legacy Core output
  helpers still need migration before #53 closes.
