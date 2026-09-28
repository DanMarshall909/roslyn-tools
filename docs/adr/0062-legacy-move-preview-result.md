# ADR-0062: Legacy move preview returns data to its caller

- Status: Accepted
- Date: 2026-09-29
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0060](0060-service-diagnostics-without-ambient-console.md)

## Context

The canonical move command already uses `MoveOperation` to return structured
results and preview data to CLI, MCP, and daemon. A legacy internal
cross-project move helper remained in `MoveService` for older tests. Its dry
run wrote source, destination, and project-file edit lines directly through
`RtResultRenderer`, coupling the shared service to process output.

## Decision

Have that helper return its preview lines in order. Its project-file edit
helper returns the matching lines without writing output. The legacy helper
is not called by production adapters, so public command output and the typed
`MoveOperation` contract do not change. Replace the old console-capture test
with assertions on the returned data. Extend the source ratchet to reject
`RtResultRenderer` calls in Services.

## Consequences

- No service calls the ambient result renderer.
- The legacy helper remains available to its internal tests while the typed
  move operation is the supported cross-host path.
- Core still contains transitional console and solution-writer output helpers;
  move or replace those before declaring #53 complete.
