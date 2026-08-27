# Design: critical platform hardening

## Canonical operation

Hosts bind input into a typed request and call one application operation. The operation owns validation, workspace acquisition, cancellation, safety policy, and service orchestration. Hosts only project the typed outcome.

```text
host adapter -> typed request -> operation router -> operation -> workspace/services
                           ^             |
                           |             +-> shared result/error/provenance envelope
                     optional daemon transport
```

The compatibility bridge is not a stable path. It is removed command-by-command as each spine command reaches parity.

## Prepared mutation protocol

`Prepare` creates an immutable `PreparedChangeSet` containing plan ID, expected revision and per-path hashes, complete filesystem/project edit plan, exact diff, diagnostics, assumptions, verification plan, and expiry. `Apply` accepts plan ID, expected revision, and a caller-generated idempotency key.

Apply validates all preconditions before writes, stages writes, atomically replaces individual files where supported, journals multi-file actions, rolls back on failure/cancellation, and returns a terminal structured result. A plan is invalid on source drift or expiry.

Transport reports `NotDispatched`, `Completed`, or `Indeterminate`. Only `NotDispatched` permits automatic retry. `Indeterminate` returns the stable operation ID and status-retrieval instructions; it never triggers fallback execution.

## Workspace and response envelope

Workspace acquisition returns `complete`, `partial`, or `failed` with project counts, failed-project identities, diagnostics, source/revision, SDK, and configuration. A partial result remains explicitly partial at top level and per affected finding. Shared policy forbids proof confidence, automatic deletion advice, and mutation suggestions from partial input.

Structured responses include schema version, request ID, status, typed data, diagnostics, provenance, confidence, next actions, and paging. Error text is rendered from typed diagnostics; raw exception details are not client contract.

## MCP and agent surface

MCP tools are individually typed and classed stable/preview/experimental. Stable tools have input/output schemas, limits, cancellation, progress where work is long-running, pagination/field projection where data is large, and protocol lifecycle handling. Capability metadata generates help, `prime`, schemas, aliases, defaults, and examples.

## Passthrough daemon boundary

The daemon carries absolute request context and correlation/cancellation IDs. It has no cache, global current-directory state, retry/recovery policy for indeterminate mutation, idempotency authority, or semantic mapping. It invokes the same operation selected by in-process execution. A parity test compares observable typed result, diagnostics, provenance, safety state, and cancellation outcome. Failure to connect before dispatch may select in-process execution; any later ambiguity is surfaced unchanged.

A future workspace daemon requires an independently approved change with isolation, session/cache keys, invalidation, memory bounds, operation ledger, health/status APIs, and multi-repository concurrency design.
