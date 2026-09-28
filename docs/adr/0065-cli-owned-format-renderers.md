# ADR-0065: Format renderers belong to the CLI adapter

- Status: Accepted
- Date: 2026-09-29
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0063](0063-cli-owned-process-output.md)

## Context

`RtSerializer` moved to CLI, but the five renderers it calls still compiled
into Core. Their only production caller is the CLI serializer. The shared
output contracts and `JsonEncoders` are consumed by other hosts and remain
reusable.

## Decision

Move `JsonRenderer`, `LlmRenderer`, `MarkdownRenderer`, `TextRenderer`, and
`YamlRenderer` unchanged into the CLI project. Retain their namespaces and
signatures so the serializer behavior and format selection do not change.
Replace two Core XML documentation references to the moved JSON renderer with
plain code text. Extend the assembly placement test to cover all five types.

## Consequences

- CLI format rendering now compiles with its caller in CLI; Core keeps the
  shared output contracts and JSON encoder selection.
- CLI already declares its direct YamlDotNet dependency, so this move adds no
  package reference.
- Ambient console/context state and legacy solution writers still need
  separate tracers before #53 closes.
