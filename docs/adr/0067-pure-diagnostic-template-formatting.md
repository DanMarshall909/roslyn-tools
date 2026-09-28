# ADR-0067: Diagnostic template formatting is independent of the console bridge

- Status: Accepted
- Date: 2026-09-29
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0061](0061-host-owned-console-logger.md)

## Context

Core's reusable `RtLoggerExtensions` called `RtConsole.RenderTemplate` only to
format named placeholders. This left its logging adapter coupled to the CLI
console bridge even when a different host supplied the logger.

## Decision

Extract the existing invariant template algorithm unchanged into
`RtMessageTemplateFormatter`. Make both the console bridge's compatibility
method and `RtLoggerExtensions` delegate to it. A source test prevents the
logger extensions from regaining a direct console dependency.

## Consequences

- Named placeholders and invariant numeric formatting keep the same output.
- Core logging helpers can format diagnostics without selecting a console or
  process output destination.
- `RtConsole`, `RtLog`, and other direct Core output paths still need separate
  tracers before #53 closes.
