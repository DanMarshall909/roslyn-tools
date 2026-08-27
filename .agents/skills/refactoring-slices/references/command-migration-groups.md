# Command migration groups

Use this reference when a refactoring slice moves a Roslyn Tools command behind a typed application port shared by CLI, daemon RPC, and MCP.

## Shared vertical shape

```text
CLI options --\
              > typed request -> application operation -> typed result/error
MCP schema ---/                         |
                                         -> Roslyn/core services
```

For every group:

1. Put request/result contracts with the application operation, not in a host adapter.
2. Keep `System.CommandLine`, console rendering, and MCP protocol types out of services.
3. Unwrap domain `Result<T, RtError>` into the port success/failure branches before RPC serialization.
4. Make the CLI bind options, invoke the shared operation through the daemon executor, and render through the shared output/error pipeline.
5. Let MCP project the generic descriptor and typed schema; do not add a bespoke handler.
6. Register the operation once and forward the same instance as `ICommandApplicationPort` when lifetime or state matters.
7. Add success, typed-error, schema/discovery, and architecture coverage before marking the catalogue entry complete.

## Group A: read-only symbol or file inspection

Examples: source, describe, outline, find references, callers, implementations, and structural pattern matching.

- Request fields identify the solution plus symbol, file, or source range.
- Validate ranges and required identifiers before loading a workspace when possible.
- Resolve symbols through injected resolution services.
- Return ambiguity and not-found outcomes as typed errors, not empty success results.
- Preserve request working directory for relative solution and file paths.
- Mark daemon safety read-only.

Representative gate: invoke through the daemon dispatcher and the generic MCP adapter; assert a populated result and an actionable ambiguous/not-found response.

## Group B: solution-wide analysis

Examples: dependency graphs, circular dependencies, duplicates, language-feature density, magic strings, and import analysis.

- Keep filters and thresholds in the typed request.
- Return structured groups, counts, and locations; rendering belongs to the host.
- Ensure deterministic ordering so CLI, MCP, and tests agree.
- Pass cancellation through workspace enumeration and document loops.
- Mark daemon safety read-only.

Representative gate: a multi-project fixture that proves aggregation, stable ordering, and a routine workspace failure mapped to a typed error.

## Group C: source mutation

Examples: rename, make sealed, add dependency, extract, move, and replace operations.

- Separate target selection, change calculation, preview, and write application.
- Model rejected or ambiguous mutations as failures even when diagnostics are returned.
- Default the descriptor to dry-run unless the command contract explicitly says otherwise.
- Return a typed mutation result that represents both preview and applied outcomes.
- Propagate request cancellation to server execution; a timed-out client must not leave a mutation writing in the background.
- Re-resolve symbols after syntax-changing solution snapshots.

Representative gate: dry-run leaves files untouched, apply changes the expected file, rejection is non-zero or typed failure, and cancellation cannot apply after the caller receives failure.

## Group D: meta or external integration

Examples: command suggestion, feedback capture, and promoting feedback to GitHub.

- Keep the operation typed even when it does not load a Roslyn workspace.
- Wrap external-process failures as actionable typed errors.
- Separate preview or search from external mutation and require an explicit apply choice.
- Make retries idempotent when an external identifier has already been recorded.
- Never include private working-directory or repository data unless it is required and intentionally selected.

Representative gate: fake the external boundary; test unavailable tooling, preview without writes, explicit apply, and idempotent retry.

## Coordinator-owned integration files

Workers should normally avoid editing these concurrently:

- daemon command catalogues and descriptor registries;
- dependency-injection bootstrap code;
- architecture classification allowlists;
- command-count or discovery manifests;
- migration tables and generated command documentation.

The coordinator reconciles them after each worker slice is green.
