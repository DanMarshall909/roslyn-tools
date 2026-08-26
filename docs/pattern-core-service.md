# Pattern: Core service + thin host adapters

A project-wide convention for software that must expose the same logic through multiple host surfaces — CLI, MCP, web API, scheduled job, library import. Validated as a proof-of-concept in [roslyn-tools](https://github.com/danmarshall/roslyn-tools) with the `suggest` (new) and `outline` (migration) tracers. Reusable wherever logic is implemented once and invoked from many places.

## The problem this solves

Most codebases that grow a second host surface follow one of two anti-patterns:

1. **The thick CLI**. All logic lives inside the CLI command class. The second host (MCP, API, …) either re-implements the same logic, or wraps the CLI and screen-scrapes its text output. Both are brittle.
2. **The leaky service**. A "service" exists in a shared library but it knows about `System.CommandLine`, `Console.WriteLine`, exit codes, or specific flag shapes. The "shared" service can't be used by anything that isn't the CLI.

Both patterns mean the second host is a second-class citizen, and changes to either host have to be reconciled twice.

The pattern below makes hosts equal-status consumers of a single canonical surface — the **Core service** — and keeps the asymmetries (rendering, CLI flag ergonomics, MCP JSON schemas) inside thin host-specific adapters.

## The four layers

```
┌─ Host adapters ─────────────────────────────────────────────────┐
│ CLI: FooCommand parses/renders │ MCP: generic adapter maps JSON │
└──────────────────────────────┬──────────────────────────────────┘
                               ▼
┌─ Application contract ──────────────────────────────────────────┐
│ FooDefinition + FooRequest + FooResult + FooOperation           │
│ safety, execution placement, typed success/error                │
└──────────────────────────────┬──────────────────────────────────┘
                               ▼
┌─ Services ──────────────────────────────────────────────────────┐
│ focused domain algorithms; no host parsing or rendering         │
└──────────────────────────────┬──────────────────────────────────┘
                               ▼
┌─ Core ──────────────────────────────────────────────────────────┐
│ Roslyn, workspace, symbol, change-writer, Result/RtError        │
└─────────────────────────────────────────────────────────────────┘
```

### Layer 1 — Pure domain service

```
src/<Project>/Services/Foo/
  FooService.cs       — internal sealed class FooService : IRtService
  FooRequest.cs       — record
  FooResponse.cs      — record
```

- **Single method** (or a small focused set) like `FooResponse Invoke(FooRequest request)`. Async if work is async.
- **Request DTO** is invocation-agnostic. No `FileInfo`, no `Console`, no host-specific types.
- **Response DTO** carries the *superset* of fields any host might want. Hosts pick what to surface.
- **Marker interface** (e.g. `IRtService` in roslyn-tools) for DI scanning.

### Layer 2 — Host-neutral application operation and DI

```csharp
// In Bootstrap.RegisterServices(IServiceCollection):
services.Scan(scan => scan
    .FromAssemblyOf<IRtService>()
    .AddClasses(classes => classes.AssignableTo<IRtService>(), false)
    .AsSelfWithInterfaces()
    .WithTransientLifetime());
```

Both hosts register the same application operations and services. The service is registered once, available to both.

Every ordinary command has a typed `ICommandApplicationPort`. The operation owns
validation, workspace lifetime, cancellation, and orchestration, then returns
`Result<TResponse, RtError>`. Anything host-specific is registered by that host.

### Layer 3 — CLI host adapter

```csharp
// src/<Project>.Cli/Commands/FooCommand.cs
internal sealed class FooCommand : IRtCommand
{
    private readonly CommandExecutionRouter _router;
    public FooCommand(CommandExecutionRouter router) => _router = router;

    public static RtCommandInfo Info { get; } = new("foo", ...);

    public Command Build()
    {
        var arg1 = new Argument<string>("arg1", "...");
        var opt1 = new Option<bool>("--opt1", "...");
        var cmd = new Command("foo", "...") { arg1, opt1 };
        cmd.SetHandler(async ctx =>
        {
            var request = BindFooRequest(ctx.ParseResult);
            await _router.ExecuteAsync(FooDefinition.Name, request, ctx);
        });
        return cmd;
    }
}
```

CLI does **only**: argument binding, calling the shared execution router, and terminal
projection. No domain logic or workspace ownership.

### Layer 4 — Generic MCP host adapter

There is no `FooHandler`. The generic MCP adapter resolves `FooDefinition`, builds a
schema from `FooRequest`, deserializes JSON, applies the shared safety policy, invokes
the same port through its execution placement, and maps structured success/error into
`ToolResult`.

### Routing

```csharp
// src/<Project>.Mcp/Tools/ToolRegistry.cs
public Task<ToolResult> InvokeAsync(string toolName, JsonElement args, CancellationToken ct) =>
    _adapter.InvokeAsync(toolName, args, ct);
```

Definitions and ports register through DI; the registry resolves them by normalized
tool name. `CliCommandBridge` exists only while the migration is partial and is deleted
at full coverage. Never add a bespoke MCP handler for an ordinary command.

## Tests, by layer

| Layer        | What to test                                        | Test cost  |
| ------------ | --------------------------------------------------- | ---------- |
| Service      | All logic. Edge cases. Property-based if relevant.  | Bulk       |
| CLI command  | Arg parsing maps to Request correctly. Output shape.| Thin       |
| MCP adapter  | Request schema, generic binding, structured result/error. | Shared |
| Integration  | End-to-end: real CLI invocation, real MCP invocation. | Smoke   |

Service tests dominate. Host tests assert wiring only — if the service is right and the wiring is right, the host's job is done.

## Asymmetries that are OK

Hosts have their own adapter layer for a reason. These differences are **expected**, not leaks:

- **CLI-only**: format flags (`--json` / `--yaml` / `--markdown`), dry-run prompts, decisions YAML files, progress logging to stderr, colour rendering, feedback capture.
- **MCP-only**: tool name normalisation (`-` → `_`), JSON schemas for inputs, native structured returns, and schema discovery via `tools/list`. Mutation defaults still come from shared safety metadata.
- **Per-feature divergence**: some commands return more detail to MCP than CLI shows by default (CLI summarises for terminals; MCP returns the full structure). That's expected — the Response DTO carries the superset and each adapter picks what to surface.

## Leakage that isn't OK

If you find these in the service, you've broken the pattern:

- `using System.CommandLine` — service depends on a CLI library.
- `Console.Write…` / `Environment.Exit` — service knows about a host's output channel.
- `JsonSerializer.Serialize(…)` inside service logic — service knows about a host's wire format.
- A `FileInfo?` parameter in the Request — service knows about a host-specific argument type. Use `string?` and let each host build the path.

A useful smoke test: **can the service be called from a unit test with no host references in scope?** If you have to add `using System.CommandLine` to the test, the service has leaked.

## Migration playbook (for existing commands)

You probably won't write everything fresh. To migrate a command that has logic inside its CLI class:

1. **Extract a service**. Move the logic into a `FooService` in Core. The CLI command becomes a parser + caller + renderer.
2. **Add a Request DTO**. Replace ad-hoc parameter lists with a record.
3. **Add an application operation**. Implement `CommandApplicationPort<TRequest,TResponse>` and return typed success/error.
4. **Thin the CLI command**. Bind the request and call the shared execution router; do not load workspaces or call services directly.
5. **Use the generic MCP adapter**. No command-specific MCP file or registration is added.
6. **Compare output**. CLI text and MCP structured data should project the same response. If they diverge unexpectedly, the divergence is the bug to fix.

Migration can fan out by the groups in
[command-application-port-migration.md](command-application-port-migration.md). The
codebase tolerates a mixed state until the bridge-deletion integration slice.

## Anti-patterns to avoid

- **God service**: cramming unrelated operations into one Core class. Split by feature, not by host.
- **Request DTO holds primitive grab-bag**: a Request with 12 nullable fields is a smell. Either split the operation, or extract sub-records.
- **Response DTO holds host-specific rendering hints**: `Color`, `IsHighlighted`, `PaddingLeft` — those are host concerns. Keep them out.
- **Service calls into host code via injected interface**: if the service depends on `IFooConsole` whose only implementation is in the CLI host, you've inverted the layering. The service should not know about hosts at all.

## Worked examples in roslyn-tools

- **`suggest` (in-process meta operation)** — `SuggestOperation` provides a typed port without requiring daemon placement.
- **`outline` (daemon-preferred inspection)** — `OutlineRequest` and `OutlineOperation` own target dispatch; CLI and MCP both use the same operation through typed routing.
- **`make-sealed` (review-by-default mutation)** — preparation, dry-run/apply, changed paths, and diagnostics are represented before either host renders them.

See [the migration playbook](command-application-port-migration.md) for the current
contract and explicit instructions for every command group. ADR-0003 and ADR-0007
supersede the historical native-handler examples retained in ADR-0001.

When adding a new feature, follow this pattern from the first commit. When touching an existing command's logic, take the opportunity to extract it.
