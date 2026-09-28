# ADR-0072: Solution writers report through a host-owned sink

- Status: Accepted
- Date: 2026-09-29
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0071](0071-extraction-diagnostics-through-logger.md)

## Context

Core's `SolutionWriter` and `SolutionChangeWriter` wrote diff, result, and
warning lines through ambient `RtConsole` and `RtLog`. That made reusable
mutation code own process output even when the caller supplied a typed writer
port. The CLI's output ordering and stderr warning channel remain part of its
observable contract.

## Decision

Define `ISolutionWriteOutput` in Core with result, diff, and warning methods.
The writers send the same lines to this sink. Shared host composition injects
`ConsoleSolutionWriteOutput`, which maps them to the existing `RtConsole`
channels and `RtLog` warning format. Direct Core calls may omit a sink and use
a no-op implementation. Remove the unused `ISolutionChangeWriter.CreateDefault`
test-only factory; callers create the writer or obtain it from host DI.

## Consequences

- Core mutation writers no longer call ambient console or logging bridges.
- CLI dry-run diff text, result lines, warning prefix, stream selection, and
  order are preserved through the host sink.
- Direct Core calls without a sink still perform or preview the same change,
  but do not render lines. Tests requiring output supply a sink explicitly.
- The existing prepared write path for non-preview changes is unchanged.
- `RtConsole` and `RtLog` can move out of Core in a later tracer once their
  remaining contracts and test placement are checked.
