# Platform hardening specification

## Requirement: canonical operation parity

Each stable command SHALL have one typed request, one application operation, and one typed result/error envelope. CLI, MCP, and the passthrough daemon SHALL call that operation without reconstructing CLI arguments or parsing rendered output.

### Scenario: converted command runs through every host

- **WHEN** a supported request for a converted command is submitted through CLI, MCP, in-process, and passthrough daemon adapters
- **THEN** each outcome has equivalent status, data, diagnostics, provenance, confidence, and safety state

## Requirement: failure-atomic reviewed mutations

A mutation SHALL prepare an immutable change set before apply. Apply SHALL verify revision and path preconditions, stage all writes, and report complete success or complete rollback.

### Scenario: stale reviewed plan is applied

- **WHEN** a file changes after a prepared change set is reviewed
- **THEN** apply rejects the stale plan before writing any path

### Scenario: write fails mid-apply

- **WHEN** a staged multi-file apply encounters a write or move failure
- **THEN** prior changes are rolled back and the result is not reported as applied

## Requirement: indeterminate dispatch is never replayed

Mutation transport SHALL classify dispatch as NotDispatched, Completed, or Indeterminate. A mutation with Indeterminate dispatch SHALL not be retried or run in-process automatically.

### Scenario: daemon response is lost after dispatch

- **WHEN** a mutation request reaches the daemon but its response is unavailable
- **THEN** the client returns an indeterminate result with operation ID and status lookup guidance
- **AND THEN** it does not send a second apply request or use in-process fallback

## Requirement: workspace completeness governs confidence

Workspace load SHALL return completeness and provenance. Partial workspaces SHALL not produce proof-strength conclusions, deletion advice, or automatic mutation suggestions.

### Scenario: project load fails during unused analysis

- **WHEN** one or more requested projects fail to load
- **THEN** find-unused returns partial status and affected findings are downgraded
- **AND THEN** no safe-delete suggestion is emitted

## Requirement: daemon is transparent in this phase

The daemon SHALL be an optional transport adapter only. It SHALL not cache workspaces, own mutation/idempotency semantics, retry after indeterminate dispatch, change the execution operation, or alter typed observable results.

### Scenario: daemon is unavailable before dispatch

- **WHEN** the transport cannot connect before request bytes are accepted
- **THEN** the host may execute the canonical operation in-process
- **AND THEN** the result retains the same operation contract and provenance

## Requirement: agent-facing contracts are executable

Stable command metadata, schemas, defaults, aliases, primer statements, and examples SHALL be generated or verified from the same capability catalogue. Invalid input SHALL be an explicit structured error.

### Scenario: an invalid enum option is supplied

- **WHEN** a user supplies an unsupported scope or mode
- **THEN** the operation returns InvalidInput
- **AND THEN** it does not return an empty successful analysis result
