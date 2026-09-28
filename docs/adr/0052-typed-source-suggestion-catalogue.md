# ADR-0052: Typed-source suggestion catalogue

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#56](https://github.com/DanMarshall909/roslyn-tools/issues/56), [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)

## Context

CLI and MCP still obtained `ICommandCatalog` from `AppCommandCatalog`, which
reflects over CLI command builders. Typed application definitions already
cover the shared public operations, while Hosting has one source for CLI-only
config, trust, and feedback metadata. `semantic-diff` is the one command whose
CLI suggestion/help projection is richer than its typed MCP definition.

## Decision

Add `SharedSuggestionCatalog` in Hosting. It reads public typed application
definitions, omits the adapter-only prime and suggest operations, and includes
shared config, trust, feedback, and rich CLI semantic-diff metadata. Keep the
typed semantic-diff operation definition unchanged for MCP. Register the new
catalogue for `ICommandCatalog` at the old CLI registration point. Defer
reading the typed catalogue until the first suggestion: resolving application
ports itself constructs services that require `ICommandCatalog`.

## Consequences

- CLI and MCP can use the same suggestion source without CLI command builders.
- A full metadata equivalence test compares all entries with the prior CLI
  catalogue; representative ranking and CLI output checks protect behavior.
- The historical `AppCommandCatalog` remains temporarily as an equivalence
  reference in tests. CLI feedback, daemon client, descriptors, and command
  builders remain CLI-owned. MCP still needs its own composition entry point
  before dropping the CLI project reference.

## Alternatives considered

- Read the typed catalogue eagerly in the constructor: rejected because it
  recurses while the catalogue resolves ports whose constructors need
  `ICommandCatalog`.
- Reuse the short typed semantic-diff description for CLI suggestions:
  rejected because it would discard existing CLI guidance and examples.
