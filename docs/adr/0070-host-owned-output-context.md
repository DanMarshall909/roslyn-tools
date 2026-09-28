# ADR-0070: Ambient output context belongs to shared hosting

- Status: Accepted
- Date: 2026-09-29
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0069](0069-explicit-symbol-suggestion-policy.md)

## Context

`RtContext` holds process-global format, verbosity, JSON escaping, workspace
warning, and suggestion flags set by CLI parsing. The CLI, MCP adapter, and
shared host composition use these flags, but Core no longer reads them after
the preceding tracer slices. Keeping the implementation in Core misstates the
dependency boundary and allows reusable code to acquire ambient host state.

## Decision

Move `RtContext` to `RoslynTools.Hosting`, preserving its internal type,
namespace, properties, and defaults so existing host adapters continue to
share the same instance. Keep the `OutputFormat` and `OutputLevel` contracts in
Core. Lock the assembly placement with an architecture test.

## Consequences

- Core no longer defines the process-global output context.
- CLI and MCP behavior remains unchanged, including shared strict JSON
  defaults and explicit relaxed JSON selection.
- The historical `RoslynTools.Core.Output` namespace remains for this internal
  type; its assembly placement is the ownership boundary.
- `RtConsole` and `RtLog` still need their own boundary slices before Core is
  free of ambient output bridges.
