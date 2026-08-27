# Critical platform hardening

- Status: proposed
- Priority: P0/P1
- Decision owner: architecture
- Supersedes for this phase: daemon workspace-caching work

## Why

The current tool is valuable, but its advertised execution safety and cross-host parity exceed the enforced runtime guarantees. The highest-risk gaps are duplicate mutation after indeterminate transport, non-atomic multi-file writes, and proof-like results from incomplete workspaces.

## Scope

This change establishes:

- a prepared-change-set mutation safety kernel with idempotency and indeterminate-dispatch handling;
- a complete/partial/failed workspace contract with provenance and confidence policy;
- a canonical typed execution spine for outline, find-refs, find-unused, rename, and safe-delete;
- structured agent/MCP envelopes, catalogue-derived metadata and executable examples;
- command admission and CI/distribution gates;
- a temporary daemon constrained to transparent transport passthrough.

## Non-goals

- Implementing a daemon-owned workspace cache, session manager, incremental invalidation, or multi-repository service.
- Broadly converting all commands before the representative vertical spine is complete.
- Replacing all tools with a generic untyped execution endpoint.

## Decision

The daemon is not an execution authority in this change. It is an optional transport adapter that forwards a typed request and returns the canonical result without semantic change. It must not own workspace state or mutations, retry after indeterminate mutation dispatch, manufacture fallback execution, or change diagnostics, result shape, cancellation, provenance, or safety policy.

## Success measures

- No mutation is automatically replayed after indeterminate dispatch.
- No proof-strength finding or deletion suggestion is emitted from a partial workspace.
- The five spine commands pass CLI/MCP/in-process/parity and failure-injection tests.
- Every stable tool has schema, provenance, confidence/limitations, bounded output, and executable examples.
- Release verification includes portable installation, protocol compatibility, spawned-host, and mutation-safety suites.
