# ADR-0060: Service diagnostics use injected logging and typed validation

- Status: Accepted
- Date: 2026-09-28
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0001](0001-core-service-thin-host-adapters.md)

## Context

Several legacy services wrote diagnostics through ambient `RtConsole` or
`RtLog`. This gave a shared service an implicit process-output dependency,
although its public command operation already used typed requests and results.
The existing architecture test only rejected direct `System.Console` calls.

## Decision

Route the remaining direct service diagnostics through the services' injected
`ILogger<T>` instances. Keep their messages and diagnostic channel when the
existing CLI logger adapter is used. Treat an unknown undocumented-symbol
accessibility level as invalid service input; the public operation already
validates it and returns a typed `RtError` before invoking the parser. Add a
source test rejecting direct `RtConsole` and `RtLog` calls in Services.

## Consequences

- Services no longer make direct ambient console or log calls for diagnostics.
- Hosts choose a logger; tests may inject a silent or capturing logger.
- The default console-logger constructor follow-up is completed by
  [ADR-0061](0061-host-owned-console-logger.md). Legacy move preview rendering
  is completed by [ADR-0062](0062-legacy-move-preview-result.md). Core output
  helpers remain separate migration work before #53 can close.
