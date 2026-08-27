---
tags:
  - architecture
  - mcp
ticket: rt-007
severity: High
status: Open
area: architecture
owner: ""
reported: 2026-07-30
summary: "CLI and MCP need one shared command descriptor and typed application port instead of CLI bridging or duplicated native handlers"
---

# Share command descriptors and application ports across CLI and MCP

## Context

`App.Commands`, `ToolSchemaBuilder`, `ToolRegistry`, `CliCommandBridge`, and native
`IMcpHandler` implementations describe overlapping command facts. Ordinary tools
screen-scrape the CLI, while native handlers repeat binding, defaults, dispatch,
and schemas. `OutlineHandler` is the clearest example: it duplicates the CLI's
file/directory/glob/namespace selection.

This issue records FB-002 as an incremental architecture migration rather than a
big-bang rewrite. ADR-0007 refines the original descriptor: the final shared
definition is host-neutral and does not retain the CLI builder.

## Acceptance Criteria

- [x] One descriptor registry owns command path, metadata, CLI builder, MCP visibility, safety, schema, and structured-result capability.
- [x] A generic MCP adapter binds descriptor input and invokes a typed application port.
- [x] CLI and MCP adapters for a migrated command invoke the same application operation.
- [x] Mutation dry-run defaults are enforced centrally.
- [x] Nested `feedback add/list` operations are modeled as leaf descriptors.
- [x] Operational commands remain MCP-hidden unless explicitly opted in.
- [x] Architecture tests verify unique tool names, schemas, structured results, and mutation safety.
- [x] Duplicated native handlers and the native-handler override path are removed.
- [ ] Shared definitions no longer contain `System.CommandLine.Command` builders or other host types.
- [ ] Request contracts are the sole source for MCP input schemas and required fields.
- [ ] The 56-command manifest has unique definition/port coverage and routing ratchets.
- [ ] Every MCP-visible descriptor has a typed application port and `CliCommandBridge` is deleted.
- [ ] MCP no longer references the CLI project; both hosts depend only on `RoslynTools`.

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-07-30 | Start with Outline and feedback tracer operations | They cover structured read-only output and nested metadata without forcing a risky all-command rewrite. |
| 2026-08-26 | Complete the migration through host-neutral definitions and grouped application-port slices | This removes the remaining physical host dependency and gives parallel conversion agents stable, non-conflicting instructions. See ADR-0007 and `docs/command-application-port-migration.md`. |
