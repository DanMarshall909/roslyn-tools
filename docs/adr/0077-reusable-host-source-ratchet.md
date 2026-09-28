# ADR-0077: Reusable source rejects host adapters and service process clients

- Status: Accepted
- Date: 2026-09-29
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0076](0076-host-owned-semantic-diff-git-client.md)

## Context

Project and compiled-assembly tests already keep CLI, MCP, daemon, and
Hosting dependencies out of Core. Incremental moves removed ambient output
bridges and command-line process adapters from reusable Services. Source-only
references could still reintroduce these host concepts without changing a
project reference immediately.

## Decision

Scan syntax trees across Core and Services for ambient output adapter names
and imports of the CLI parser, terminal renderer, MCP SDK, and generic host
framework. Also reject process launch types in Services, where the concrete
`gh` and semantic diff `git` clients previously lived. Existing project,
assembly, placement, direct-console, and parser-import tests remain separate
checks of their respective boundaries.

## Consequences

- Reusable source cannot regain ambient `RtConsole`, `RtLog`, `RtContext`, or
  CLI result renderer calls without a failing architecture test.
- Services cannot construct a `Process` or `ProcessStartInfo`; shared host
  adapters own command-line process clients.
- Core's repository trust and workspace proof git subprocesses remain in
  their established reusable security infrastructure. This ratchet does not
  alter those contracts.
- A temporary probe containing a forbidden import, ambient log call, and
  process type made the test fail on all three, then passed after removal.
