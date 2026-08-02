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
big-bang rewrite.

## Acceptance Criteria

- [x] One descriptor registry owns command path, metadata, CLI builder, MCP visibility, safety, schema, and structured-result capability.
- [x] A generic MCP adapter binds descriptor input and invokes a typed application port.
- [x] CLI and MCP adapters for a migrated command invoke the same application operation.
- [x] Mutation dry-run defaults are enforced centrally.
- [x] Nested `feedback add/list` operations are modeled as leaf descriptors.
- [x] Operational commands remain MCP-hidden unless explicitly opted in.
- [x] Architecture tests verify unique tool names, schemas, structured results, and mutation safety.
- [x] Duplicated native handlers and the native-handler override path are removed.
- [ ] Every MCP-visible descriptor has a typed application port and `CliCommandBridge` is deleted.

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-07-30 | Start with Outline and feedback tracer operations | They cover structured read-only output and nested metadata without forcing a risky all-command rewrite. |
