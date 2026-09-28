# ADR-0064: Service outcomes do not set process exit status

- Status: Accepted
- Date: 2026-09-29
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0060](0060-service-diagnostics-without-ambient-console.md)

## Context

`AddDependencyService` still set `Environment.ExitCode` when a target was
missing, ambiguous, or unsupported. The current CLI and daemon paths use
`AddDependencyOperation.PrepareAsync`, which already returns typed outcomes and
maps failures to `RtError`. A service changing process state could also affect
unrelated invocations in a long-lived host.

## Decision

Remove the four process exit assignments from `AddDependencyService`. Keep its
existing outcome and diagnostic behavior. The CLI adapter maps application
errors to invocation exit status. Add a source architecture test that rejects
service assignments to `Environment.ExitCode`, and assert that legacy service
calls leave an existing process exit status alone.

## Consequences

- The CLI and daemon continue to report add-dependency failures through the
  typed application port and host exit handling.
- Direct callers of the legacy service `ApplyAsync` must inspect its outcome;
  they no longer receive an implicit process exit side effect. No production
  caller uses that method.
- Other ambient Core state and legacy solution writers still require tracers
  before #53 closes.
