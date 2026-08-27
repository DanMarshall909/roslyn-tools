# Tasks: critical platform hardening

## P0 safety kernel

- [ ] Define `PreparedChangeSet`, plan expiry, revision/hash preconditions, verification plan, and terminal apply result.
- [ ] Implement staged write, atomic replacement, rollback journal, stale-plan rejection, and cancellation tests.
- [ ] Add caller-generated operation IDs, idempotency records, status lookup, and `NotDispatched | Completed | Indeterminate` transport outcomes.
- [ ] Block automatic mutation retry and in-process fallback after indeterminate dispatch.
- [ ] Disable apply-capable bridged MCP mutations until they use the kernel.
- [ ] Define and enforce workspace completeness/provenance; downgrade partial findings and suppress deletion/mutation advice.

## P1 canonical spine and contracts

- [ ] Convert outline, find-refs, find-unused, rename, and safe-delete to one request/operation/result contract.
- [ ] Remove CLI bridge paths for each converted command and prove CLI/MCP/in-process/passthrough parity.
- [ ] Replace stringly validated options with typed validation; invalid input must be a structured error.
- [ ] Generate primer, metadata, schemas, aliases/defaults, and examples from one catalogue.
- [ ] Implement stable MCP schemas, structured response envelope, cancellation, bounded input/output, pagination/field projection, lifecycle/protocol negotiation.
- [ ] Classify tool maturity and hide bridge-backed mutations by default.

## Daemon boundary

- [ ] Mark workspace caching/session ownership as parked and superseded for this phase.
- [ ] Make the daemon a transparent typed transport only; remove global-CWD request semantics and any semantic fallback.
- [ ] Add tests proving no cache, no mutation ownership, no indeterminate retry, and exact observable parity.
- [ ] Require a separate approved OpenSpec change before proposing daemon workspace caching.

## CI, distribution, and admission

- [ ] Execute every published command example against fixtures.
- [ ] Add fault-injection tests for write failure, rename conflict, cancellation, lost response, and partial workspace.
- [ ] Add spawned CLI/MCP/daemon protocol matrix, portable install/SDK/runtime checks, and release smoke tests.
- [ ] Add architecture tests that reject new stable commands lacking the admission evidence.
- [ ] Document evidence and residual risks in release notes.
