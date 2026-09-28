# ADR-0073: Ambient console and logging bridges belong to shared hosting

- Status: Accepted
- Date: 2026-09-29
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0072](0072-solution-writer-report-sink.md)

## Context

After service diagnostics and solution write reports acquired explicit ports,
Core no longer called `RtConsole` or `RtLog`. Their implementations and
supporting console types still compiled into Core, leaving direct process I/O
in the reusable assembly.

## Decision

Move `RtConsole`, `RtLog`, `IRtConsole`, `IRtLogger`, `RtLogLevel`, and
`RtOutputContext` into shared Hosting without changing their definitions or
internal namespaces. Keep the pure `RtMessageTemplateFormatter` in Core.
Require an architecture placement test and limit the direct `System.Console`
exception to the bridge's new Hosting paths.

## Consequences

- Core no longer contains an ambient process console or logging bridge.
- CLI, MCP, and daemon retain the same shared console state and output behavior.
- The historical `RoslynTools.Core` namespace remains on these internal types
  for source compatibility; assembly placement defines ownership.
- The Core public-method review index no longer tracks methods moved outside
  the Core source tree.
