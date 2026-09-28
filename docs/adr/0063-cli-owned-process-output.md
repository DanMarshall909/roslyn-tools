# ADR-0063: CLI process output implementations belong to CLI

- Status: Accepted
- Date: 2026-09-29
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0057](0057-host-dependency-and-contract-ratchets.md)

## Context

Core still compiled three CLI-only output implementations after service-level
renderer calls were removed. `RtResultRenderer` sets process exit status and
writes to the console; `RtSerializer` selects the CLI's active format and
writer; `StructuredOutput` writes JSON or YAML to stdout. No reusable service
or MCP/daemon adapter calls these implementations.

## Decision

Move all three implementations unchanged into the CLI project, retaining
their namespaces and signatures. Keep shared output contracts and pure
formatting helpers in Core for now. Declare the CLI's direct YamlDotNet use
in its project file. Add an assembly placement test to prevent the three
implementations from returning to Core.

## Consequences

- The reusable assembly no longer implements these process output actions.
- CLI formatting and public output remain unchanged; CLI output tests cover
  text, JSON, YAML, and relaxed JSON after the move.
- Other ambient output helpers still in Core, particularly `RtConsole`,
  `RtLog`, `RtContext`, and legacy solution writers, require further tracers
  before #53 closes.
