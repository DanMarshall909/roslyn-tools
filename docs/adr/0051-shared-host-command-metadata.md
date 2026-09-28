# ADR-0051: Shared metadata for host-only suggestions

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#56](https://github.com/DanMarshall909/roslyn-tools/issues/56), [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)

## Context

The suggestion catalogue includes CLI-only `config` and `trust` commands plus
feedback guidance. Their `RtCommandInfo` values were declared in CLI command
types or appended inside the CLI catalogue. A peer host needs the same
metadata to preserve suggestion behavior after it stops referencing CLI.

## Decision

Place these three `RtCommandInfo` values in `HostCommandMetadata` in the
shared Hosting assembly. The CLI `ConfigCommand.Info` and `TrustCommand.Info`
properties expose those same values, and `AppCommandCatalog` appends the shared
feedback value. Keep all metadata text, examples, formats, and related-command
links unchanged. An architecture test pins the source identity.

## Consequences

- Future peer-host suggestion catalogues can include CLI-only guidance from
  the same source without duplicating metadata.
- CLI public help and suggestion ranking retain their current values.
- The next #56 tracer constructed the full suggestion list from typed
  application definitions and these shared host entries. It also moved the
  richer CLI `semantic-diff` metadata here without changing its typed port.

## Alternatives considered

- Copy these entries into MCP: rejected because duplicated examples and
  guidance would drift from CLI help.
