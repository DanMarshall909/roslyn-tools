# Command application-port migration playbook

This is the fan-out specification for converting the complete public command surface
to the host-neutral application-port architecture in
[ADR-0007](adr/0007-host-neutral-command-contracts.md).

## Scope and baseline

The machine-readable catalog currently contains 56 category commands:

| Category | Total | Port-backed | Remaining |
|---|---:|---:|---:|
| Inspect | 9 | 6 | 3 |
| Analyze | 14 | 6 | 8 |
| Refactor | 33 | 3, including partial `rename` | 30 plus `rename` completion |
| **Total** | **56** | **15** | **41 conversion/completion slices** |

`suggest` and the feedback leaves are already typed meta operations but are outside
the 56 category commands. Operational commands such as `daemon`, `prime`, and
`dump-help` remain host-owned.

## Target flow

```text
CLI arguments ──> CLI binder ──┐
                               ├─> typed Request ─> application port ─> typed Result
MCP JSON ──> generic adapter ───┘                         │
                                                         ├─ in process
                                                         └─ typed daemon RPC
```

There is no command-specific MCP handler and no CLI output capture.

## Serial foundation — complete before fan-out

One integration owner lands these shared changes. Conversion agents must not invent
local substitutes.

1. **Host-neutral definitions**
   - Introduce one application definition per ordinary command containing
     `RtCommandInfo`, normalized identity, request/response types, visibility, safety,
     execution placement, and suggested tools.
   - Discover definitions and ports automatically through DI/reflection.
   - Remove the requirement to add every migrated command to
     `DaemonOwnedCommands` manually.

2. **Port result contract**
   - Make the typed port base return `Result<TResponse, RtError>` and unwrap it into
     `CommandPortResult` at the boundary.
   - Expose `ResponseType` as well as `RequestType`.
   - Treat cancellation as cancellation, not a successful error payload.

3. **Request-owned schema**
   - Generate MCP properties and required fields from request records.
   - Use `JsonPropertyName`, `JsonRequired`, and a host-neutral description attribute.
   - Stop instantiating `System.CommandLine.Command` inside `ToolSchemaBuilder`.

4. **Execution placement and recovery**
   - Define `DaemonPreferred` and `InProcess` placement.
   - Share daemon start/retry/fallback between CLI and MCP.
   - Distinguish unavailable-before-dispatch from indeterminate-after-dispatch.
   - Never retry or fall back for a mutation that may already have been accepted.

5. **Mutation envelope**
   - Support `Reviewed`, `DryRun`, `Applied`, and `NoOp` as distinct statuses.
   - Carry a typed domain value, every affected path, and structured diagnostics.
   - Add a prepared change-set abstraction for multi-file and filesystem mutations.

6. **Host-neutral suggested actions**
   - Move `SuggestedFix`, `FixCatalog`, and related records out of CLI.
   - Keep suggestions as data. Rendering command lines is an adapter concern.

7. **Ratchets**
   - Freeze the 56-command inventory.
   - Assert one definition and one port for each migrated command.
   - Assert unique normalized names, safety consistency, request schema parity, and no
     host types in request/result contracts.

## Canonical command slice

### Contracts and operation

```csharp
internal sealed record FooRequest(
    [property: JsonRequired] string Target,
    string? Solution = null);

internal sealed record FooResult(
    string Target,
    IReadOnlyList<FooFinding> Findings);

internal sealed class FooOperation
    : CommandApplicationPort<FooRequest, FooResult>, IRtService
{
    public override ApplicationCommandDefinition Definition => FooDefinition.Value;

    public override async Task<Result<FooResult, RtError>> ExecuteAsync(
        FooRequest request,
        CancellationToken cancellationToken = default)
    {
        // validate, own workspace lifetime, call injected services, return typed data
    }
}
```

Rules:

- Request names are transport-neutral domain names, not flag-interface types.
- Operations own validation, workspace/session lifetime, cancellation, and orchestration.
- Existing algorithms stay in focused services/analyzers; do not create god operations.
- Routine missing/ambiguous/invalid/unsupported outcomes return `RtError`.
- No `System.CommandLine`, `InvocationContext`, `JsonElement`, serializer calls,
  `RtConsole`, `RtLog`, `RtResultRenderer`, `Environment.ExitCode`, anonymous output,
  or pre-rendered JSON/DOT belongs in an operation or domain service.

### CLI adapter

The command owns only metadata projection, arguments/options, binding, and terminal
projection:

```csharp
cmd.SetHandler(async context =>
{
    var request = binder.Bind(context.ParseResult, FooCliRequest.FromFlags)
        .ToApplicationRequest();
    await commandRouter.ExecuteAsync(FooDefinition.Name, request, context);
});
```

- Piped stdin is resolved before creating the application request.
- CLI-only format selection, progress, colour, and exit-code mapping stay here.
- Do not load a workspace or call the domain service from the command.

### MCP adapter

No command-local MCP file is added. The generic adapter:

1. finds the host-neutral definition;
2. deserializes its request;
3. applies the shared mutation safety default;
4. routes through the declared execution policy; and
5. maps structured success/error into MCP output.

## Read-only groups

### R0 — reconcile existing ports

Commands:

- `outline`, `describe`, `find-callers`, `find-impl`, `find-refs`, `source`
- `circular-deps`, `find-magic-strings`, `list-imports`, `semantic-diff`

Instructions:

- Migrate each to the final definition/result base; do not rewrite proven algorithms.
- Route the four locally invoked CLI operations through the shared execution router:
  `describe`, `find-refs`, `source`, and `semantic-diff`.
- Replace heterogeneous/dictionary results where feasible:
  - `outline`: replace `List<object>` with a typed envelope;
  - `find-impl`: replace implementation dictionaries;
  - `source`: replace `List<Dictionary<string, object?>>`;
  - preserve `describe` compact/verbose as an explicit discriminated result because
    its two-shape contract is intentional.
- Make symbol-resolution injection mandatory; no operation may construct a fallback
  concrete resolver.
- Remove command-held operation/service fields used only as legacy test seams.
- Normalize all expected errors through the shared `Result` base rather than custom
  `ICommandApplicationPort.InvokeAsync` implementations.

Execution placement:

- DaemonPreferred: all except `semantic-diff`.
- InProcess: `semantic-diff` because it compares git/files without a Roslyn workspace.

### R1 — solution inventories

Commands: `find-unused`, `find-undocumented`, `static-class-inventory`.

Requests:

```text
FindUnusedRequest(Solution, Scope)
FindUndocumentedRequest(Solution, Members, Scope, Level)
StaticClassInventoryRequest(Solution)
```

Instructions:

- Operation injects `IWorkspaceSessionFactory` and the existing matching service.
- Move `--level` validation from `FindUndocumentedCommand` into the operation.
- Move the three result contracts from `Commands/Results/` into the shared layer.
- Preserve solution path, confidence/reason, and suggested-action data in responses.
- Do not build safe-delete command strings inside domain services.

Execution: DaemonPreferred, read-only.

### R2 — service-backed analysis reports

Commands: `analyze-interface`, `find-duplicates`, `probe-di`.

Requests:

```text
AnalyzeInterfaceRequest(Interface, Solution, Scope, IncludeTests, Suggest)
FindDuplicatesRequest(Solution, MinStatements, MinOccurrences, Scope, IncludeTests, MaxStatements)
ProbeDiRequest(Entrypoint, Solution)
```

Instructions:

- Convert interface not-found, ambiguity, and wrong-kind outcomes into `RtError`.
- `FindDuplicatesOperation` owns `FindDuplicatesOptions` and solution-directory
  calculation.
- Move `ProbeDiFinding` and `ProbeDiResult` out of the CLI command.
- Move shared suggested-action contracts before this group starts.
- Keep assembly loading isolated and collectible.

Execution:

- DaemonPreferred: `analyze-interface`, `find-duplicates`.
- InProcess: `probe-di` until loading user assemblies into the resident daemon has an
  explicit unload/isolation proof.

### R3 — static analyzer reports

Commands: `complexity`, `language-features`, `list-smells`, `solid`.

Requests:

```text
ComplexityRequest(Solution, Threshold, Sort, Top)
ListSmellsRequest(Solution, Scope, Top, MaxClassLines, MaxMethodLines, MaxParams)
SolidRequest(Solution, Principle, MinMethods, MinDeps, MinMembers, MinBranches)
```

Instructions:

- Operations own calls to `ComplexityAnalyzer`, `SmellAnalyzer`, and `SolidAnalyzer`.
- Move threshold, sort, top, and principle validation out of CLI.
- Remove service-provider lookup and default workspace construction.
- Move result graphs from `Commands/Results/` into shared typed records.
- Move progress logging to CLI; operations materialize results without rendering.
- `list-smells` returns suggested fixes as typed data.

Execution: DaemonPreferred, read-only.

### R4 — structural source analysis

Commands: `find-pattern`, `dependency-graph`.

Requests:

```text
FindPatternRequest(File, StartLine, EndLine, Solution, Scope)
DependencyGraphRequest(Solution, Type, Depth, Scope)
```

Instructions:

- Validate positive/increasing line ranges in `FindPatternOperation`.
- Convert missing files and invalid statement blocks into typed errors.
- Return typed graph nodes/edges from `DependencyGraphOperation`.
- DOT, text, JSON, and Markdown are renderer projections, not service results.
- Remove `Environment.ExitCode` and pre-rendered strings from shared logic.

Execution: DaemonPreferred, read-only.

### R5 — file-only trace analysis

Command: `debug`.

Request:

```text
DebugRequest(Trace, From, Limit, Breakpoints, Assertions, BreakOnly)
```

Instructions:

- Reconcile the existing `DebugOperation` to the final definition/result base after
  US-143 lands; do not rewrite the trace analyzer.
- Keep the string path and array-valued breakpoint/assertion properties in
  `DebugRequest`; do not introduce `FileInfo` into the application request.
- Preserve the typed `DebugReport`; keep terminal rendering in CLI.
- File-not-found and malformed JSON are typed errors.
- Failed assertions remain a successful analysis response; CLI alone maps them to
  exit code 2.

Execution: InProcess, read-only.

## Mutation groups

All mutations return `MutationExecutionResult<T>` or its finalized replacement.
Every preview lists all affected paths. Apply happens once, after preparation succeeds.

### M0 — reconcile reference ports and complete rename

Commands: `add-dependency`, `make-sealed`, `rename`.

Instructions:

- Freeze `add-dependency` and `make-sealed` as golden mutation tracers, then migrate
  them mechanically to the final definition/result base.
- Complete `rename`:
  - model single-symbol and CSV batch modes explicitly;
  - resolve piped stdin in CLI binding;
  - move batch workspace ownership into the operation;
  - return the related-edit plan in the typed result;
  - remove direct rendering and filesystem side effects from the service; and
  - route CLI through the shared execution router.
- Preserve current public apply defaults. MCP safety projection remains centralized.

Execution: DaemonPreferred, mutation.

### M1 — mechanical one-workspace mutations

Commands:

- `add-dynamic-dependency`, `add-global-import`, `add-null-checks`,
  `add-static-import`, `add-xml-doc`
- `convert-to-record`, `convert-static-class`, `extract-interface`,
  `extract-string-constant`, `fix-namespace`, `implement-interface`, `inline-method`
- `make-async`, `make-instance`, `make-static`
- `replace-call`, `replace-new`, `replace-datetime`, `safe-delete`

Instructions:

- Add one request/result/operation set per public command.
- Operation owns workspace load, preparation, cancellation, and one writer call.
- Refactor the existing service toward `PrepareAsync` returning updated solution,
  changed paths, typed outcome, and diagnostics.
- Convert legacy console no-op messages into `NoOp` diagnostics.
- Specific cases:
  - import pair may share preparation helpers but remain separate ports;
  - `add-xml-doc` carries a JSON path or typed map and resolves it under request cwd;
  - move workspace ownership out of `FixNamespaceService`;
  - replace `MakeStaticService` integer/exit-code outcomes with typed failures;
  - preserve safe-delete evidence in the result.

Execution: DaemonPreferred, apply-by-default mutation unless the existing command
explicitly documents another default.

### M2 — discriminated multi-mode mutations

Commands: `add-test`, `change-signature`, `transform-parameters`.

Modes:

- `add-test`: legacy test generation vs command-scaffold generation;
- `change-signature`: rename, reorder, add, remove;
- `transform-parameters`: new-type vs existing-type.

Instructions:

- Use an explicit mode enum plus mode-specific payload records or a validated
  discriminated request. Do not create a dozen-nullable-field grab bag.
- Keep one application port per public command unless the CLI exposes independently
  discoverable command leaves.
- Operation owns mode validation and returns typed invalid-mode errors.
- Remove `Environment.ExitCode` and command-handler orchestration from shared logic.

Execution: DaemonPreferred, apply-by-default mutation.

### M3 — review-first mutations

Commands: `convert-console-logging`, `replace-xunit-asserts`.

Execution modes: `Review`, `DryRun`, `Apply`.

Instructions:

- Default review performs analysis only and returns typed findings.
- Dry-run prepares the transformation and returns the exact affected paths/diff data.
- Apply executes the same preparation once and writes it.
- Do not represent review as dry-run.
- `convert-console-logging` composes dependency injection and logging replacement into
  one preparation/write boundary.
- Replace `StringBuilder`, prose result, and command-owned rendering with typed data.

Execution: DaemonPreferred, review-by-default mutation.

### M4 — high-impact multi-file and filesystem mutations

Commands: `extract-class`, `extract-method`, `migrate-to-ioptions`, `move`,
`move-method`, `narrow-dependency`.

Instructions:

- Use the shared prepared change-set abstraction for solution changes, new/deleted
  files, file moves, and project-file edits.
- Preview reports every affected path; apply executes the prepared set once.
- Cancellation or a write failure must not leave a half-applied raw `File.Move` chain.
- Specific cases:
  - `extract-method` retains replace-duplicates diagnostics;
  - `migrate-to-ioptions` returns discovery/conflict/already-applied outcomes and wraps
    appsettings reads;
  - `move` has explicit namespace, single-type, and cross-project modes;
  - `move-method` replaces boolean outcomes with a typed outcome;
  - `narrow-dependency` returns a structured summary;
  - `extract-class` replaces exit-code mutation with typed failure.

Execution: DaemonPreferred, apply-by-default mutation.

## Fan-out ownership rules

Each conversion agent owns only:

- its assigned command file(s);
- new request/result/operation files;
- the directly related service changes; and
- focused operation and thin-adapter tests.

The integration owner exclusively owns:

- application definition/discovery and DI infrastructure;
- daemon routing/recovery and mutation change-set abstractions;
- schema generation;
- architecture ratchets and coverage manifests;
- shared suggested-action contracts;
- documentation/PRD updates; and
- final bridge deletion/project-reference changes.

Do not have every group edit a central daemon catalog, descriptor allow-list, generated
snapshot, or project file in parallel.

## Required tests for every converted command

1. Request binding and schema: names, required properties, defaults, arrays/enums.
2. Operation happy path against focused Roslyn fixtures.
3. Expected invalid/not-found/ambiguous/unsupported failure.
4. Cancellation propagation.
5. CLI exit and renderer projection.
6. MCP structured success/error through the generic adapter.
7. Daemon and in-process parity for daemon-preferred commands.
8. Mutations only: review/dry-run/apply/no-op as applicable, complete changed-path list,
   and no write during preview.

## Definition of done

- Every one of the 56 category commands appears in exactly one group above.
- Every MCP-visible ordinary definition has exactly one typed port.
- `CliCommandBridge`, `CapturingConsole`, native-handler override routing, nullable-port
  branches, and the manual daemon command list are deleted.
- MCP does not reference CLI; neither host references the other.
- Shared code contains no `System.CommandLine`, host protocol, console, renderer, or
  exit-code dependency.
- Full architecture, CLI/core, MCP, distribution, parity, coverage, and mutation gates
  pass.
