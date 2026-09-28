# ADR-0078: One typed change-signature operation across CLI and MCP

- Status: Accepted
- Date: 2026-09-29
- Related issues: [#21](https://github.com/DanMarshall909/roslyn-tools/issues/21), [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)

## Context

The CLI already exposed four `change-signature` subcommands through the shared
`ChangeSignatureOperation`, but MCP discovery could not describe or call each
leaf unambiguously. The request used a `mode` enum with CLR member names, while
the agent-facing CLI names use kebab case. That mismatch made the generated
schema unsuitable as the MCP input contract.

## Decision

The shared request requires an `operation` discriminator with four wire values:
`rename-param`, `reorder`, `add-param`, and `remove-param`. Its application
definition supplies a `oneOf` request branch per operation, naming required
fields and ruling out unrelated fields. The generic MCP schema builder reads
`JsonStringEnumMemberName` values, so schema literals match JSON binding. The
generic MCP adapter validates declared request variants against raw arguments
before dispatch, including required and forbidden fields. Numeric enum values
are rejected. It then binds the request directly to the same typed port used
by CLI; no leaf-specific MCP handler is introduced. MCP metadata projects the
canonical CLI examples from the same `RtCommandInfo` source.

## Consequences

- MCP discovery has one `change_signature` tool with four explicit input shapes.
- Missing or invalid operation values fail as invalid input before workspace
  acquisition; dry run remains the default for all four leaves.
- The daemon and MCP request wire property changes from `mode` to `operation`.
  Long-lived daemon and MCP processes need restart after upgrading both hosts.
- The result's existing `mode` field keeps its PascalCase enum values for
  consumers of CLI, daemon, and MCP output.
- CLI output keeps its existing `data` envelope; MCP returns the same typed
  mutation result as `structuredContent`. Their host projections differ in
  status representation, and tests compare the underlying result fields.
