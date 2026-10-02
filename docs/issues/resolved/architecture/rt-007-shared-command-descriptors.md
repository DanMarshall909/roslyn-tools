---
tags:
  - architecture
  - mcp
ticket: rt-007
github_issue: https://github.com/DanMarshall909/roslyn-tools/issues/14
severity: High
area: architecture
owner: ""
reported: 2026-07-30
resolved: 2026-09-29
summary: "CLI and MCP need one shared command descriptor and typed application port instead of CLI bridging or duplicated native handlers"
---

# Share command descriptors and application ports across CLI and MCP

## Original context

At filing, `App.Commands`, `ToolSchemaBuilder`, `ToolRegistry`,
`CliCommandBridge`, and native `IMcpHandler` implementations described
overlapping command facts. Ordinary tools screen-scraped the CLI, while native
handlers repeated binding, defaults, dispatch, and schemas. `OutlineHandler`
was the clearest example: it duplicated the CLI's file/directory/glob/namespace
selection.

This issue records FB-002 as an incremental architecture migration rather than a
big-bang rewrite. ADR-0007 refines the original descriptor: the final shared
definition is host-neutral and does not retain the CLI builder.

## Acceptance Criteria

- [x] Host-neutral definitions own command identity, metadata, MCP visibility, safety, request/result contracts, and variants; CLI builders remain in the CLI adapter.
- [x] A generic MCP adapter binds descriptor input and invokes a typed application port.
- [x] CLI and MCP adapters for a migrated command invoke the same application operation.
- [x] Mutation dry-run defaults are enforced centrally.
- [x] Nested `feedback add/list` operations are modeled as leaf descriptors.
- [x] Operational commands remain MCP-hidden unless explicitly opted in.
- [x] Architecture tests verify unique tool names, schemas, structured results, and mutation safety.
- [x] Duplicated native handlers and the native-handler override path are removed.
- [x] Shared definitions contain no `System.CommandLine.Command` builders or other host types.
- [x] Typed request contracts and declared variants drive MCP-visible input schemas and required fields.
- [x] The command migration manifest has unique definition/port coverage and routing ratchets.
- [x] Every MCP-visible descriptor has a typed application port and `CliCommandBridge` is deleted.
- [x] MCP does not reference the CLI project; CLI and MCP compose the same Core ports through shared Hosting.

## Archive decision

The typed-port and host-decoupling workstreams are complete. The final tracer
bullets exposed all four `change-signature` leaves through one MCP tool
([ADR-0078](../../../adr/0078-change-signature-operation-discriminator.md),
commit `8857f24`) and compared read-only, structured, and mutating results across
CLI and MCP (commit `d7fbc92`).

`CommandDescriptorContracts` asserts that every discoverable MCP command has a
non-legacy typed port and that the bridge and bespoke handler path are absent.
`HostDependencyRatchetTests` checks project and compiled-assembly boundaries and
compares CLI, MCP, and daemon typed contracts. `ReusableHostSourceBoundaryTests`
and `ReusableProjectCommandLineBoundary` keep parser, protocol, ambient output,
and service process clients out of reusable source. The local gate passed with
3,152 core/CLI tests, 9 intentional skips, and clean formatting; MCP Release
passed 224 tests. GitHub [#14](https://github.com/DanMarshall909/roslyn-tools/issues/14)
owns the live workflow state, with the broader decoupling work tracked by
[#53](https://github.com/DanMarshall909/roslyn-tools/issues/53).

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-07-30 | Start with Outline and feedback tracer operations | They cover structured read-only output and nested metadata without forcing a risky all-command rewrite. |
| 2026-08-26 | Complete the migration through host-neutral definitions and grouped application-port slices | This removes the remaining physical host dependency and gives parallel conversion agents stable, non-conflicting instructions. See ADR-0007 and `docs/command-application-port-migration.md`. |
| 2026-09-29 | Archive the migration record after typed-port, host-boundary, and equivalence acceptance passed | The generic MCP adapter, shared typed catalogue, and source/dependency ratchets now enforce the target architecture; GitHub owns ongoing status. |
| 2026-10-03 | Reconcile the archived migration with broader canonical-contract admission | The [generated status view](../../../command-contract-status.md) reads this record, the migration manifest, and platform-hardening P1.1 without treating unchecked admission tasks as evidence of a surviving CLI bridge. Historical gate evidence and archive state are unchanged. |
