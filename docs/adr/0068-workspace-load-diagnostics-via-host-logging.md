# ADR-0068: Workspace load diagnostics use host logging and warning policy

- Status: Accepted
- Date: 2026-09-29
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0066](0066-workspace-cache-diagnostics-through-host.md)

## Context

Core's `WorkspaceLoader` directly read `RtContext.ShowWorkspaceWarnings` and
called `RtLog` for workspace failures and warnings. Those globals represented
CLI invocation state and output selection inside a reusable workspace service.

## Decision

Give host-composed loader instances an injected logger and warning-policy
callback. Shared host composition supplies its console-backed logger and reads
the current CLI warning flag. The loader retains its diagnostic grouping and
severity, but emits through the injected logger. Standalone test and
compatibility construction uses a silent logger with warnings off; the
test-only diagnostic writer override remains available. A source test rejects
direct `RtLog` and `RtContext` access in `WorkspaceLoader`.

## Consequences

- CLI warning selection and diagnostic text remain the same through host
  composition, while Core no longer chooses ambient logging or a CLI flag.
- Other Core helpers still use ambient state; #53 remains open for them.
