# Pattern: Core service + thin host adapters

A project-wide convention for software that must expose the same logic through multiple host surfaces — CLI, MCP, web API, scheduled job, library import. Validated as a proof-of-concept in [roslyn-tools](https://github.com/danmarshall/roslyn-tools) with the `suggest` (new) and `outline` (migration) tracers. Reusable wherever logic is implemented once and invoked from many places.

## The problem this solves

Most codebases that grow a second host surface follow one of two anti-patterns:

1. **The thick CLI**. All logic lives inside the CLI command class. The second host (MCP, API, …) either re-implements the same logic, or wraps the CLI and screen-scrapes its text output. Both are brittle.
2. **The leaky service**. A "service" exists in a shared library but it knows about `System.CommandLine`, `Console.WriteLine`, exit codes, or specific flag shapes. The "shared" service can't be used by anything that isn't the CLI.

Both patterns mean the second host is a second-class citizen, and changes to either host have to be reconciled twice.

The pattern below makes hosts equal-status consumers of a single canonical surface — the **Core service** — and keeps the asymmetries (rendering, CLI flag ergonomics, MCP JSON schemas) inside thin host-specific adapters.

## The five layers

```
┌─────────────────────────────────────────────────────────────────┐
│ Host (CLI)                          │ Host (MCP)                │
│ ┌─────────────────┐                 │ ┌─────────────────┐       │
│ │  FooCommand     │                 │ │  FooHandler     │       │
│ │  - parses CLI   │                 │ │  - parses JSON  │       │
│ │  - renders text │                 │ │  - returns JSON │       │
│ └────────┬────────┘                 │ └────────┬────────┘       │
│          │                           │          │                │
│          ├──────── DI resolves ──────┴──────────┤                │
│          ▼                                      ▼                │
│ ┌──────────────────────────────────────────────────────┐         │
│ │  FooService : IRtService            (Core)            │         │
│ │  Invoke(FooRequest) → FooResponse                    │         │
│ │  • No System.CommandLine                              │         │
│ │  • No Console                                         │         │
│ │  • No IO except via injected dependencies            │         │
│ └──────────────────────────────────────────────────────┘         │
└─────────────────────────────────────────────────────────────────┘
```

### Layer 1 — Pure service in Core

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

### Layer 2 — DI registration

```csharp
// In Bootstrap.RegisterServices(IServiceCollection):
services.Scan(scan => scan
    .FromAssemblyOf<IRtService>()
    .AddClasses(classes => classes.AssignableTo<IRtService>(), false)
    .AsSelfWithInterfaces()
    .WithTransientLifetime());
```

Both hosts call the same `Bootstrap.RegisterServices(sc)` from their respective `Program.cs`. The service is registered once, available to both.

Anything host-specific (e.g. an `ICommandCatalog` whose source is `App.Commands` in the CLI but might be a different source elsewhere) is registered by each host individually.

### Layer 3 — CLI host adapter

```csharp
// src/<Project>.Cli/Commands/FooCommand.cs
internal sealed class FooCommand : IRtCommand
{
    private readonly FooService _foo;
    public FooCommand(FooService foo) => _foo = foo;

    public static RtCommandInfo Info { get; } = new("foo", ...);

    public Command Build()
    {
        var arg1 = new Argument<string>("arg1", "...");
        var opt1 = new Option<bool>("--opt1", "...");
        var cmd = new Command("foo", "...") { arg1, opt1 };
        cmd.SetHandler(ctx =>
        {
            var request = new FooRequest(
                Arg1: ctx.ParseResult.GetValueForArgument(arg1),
                Opt1: ctx.ParseResult.GetValueForOption(opt1));
            var response = _foo.Invoke(request);
            Render(response);
        });
        return cmd;
    }
}
```

CLI does **only**: arg parsing, calling the service, rendering output. No logic.

### Layer 4 — MCP host adapter

```csharp
// src/<Project>.Mcp/Handlers/FooHandler.cs
internal sealed class FooHandler : IMcpHandler
{
    private readonly FooService _foo;
    public FooHandler(FooService foo) => _foo = foo;

    public string ToolName => "foo";
    public ToolDefinition Definition => new(ToolName, "...", schema);

    public Task<ToolResult> HandleAsync(JsonElement args, CancellationToken ct)
    {
        var request = new FooRequest(
            Arg1: args.GetProperty("arg1").GetString()!,
            Opt1: args.TryGetProperty("opt1", out var o) && o.GetBoolean());
        var response = _foo.Invoke(request);
        return Task.FromResult(new ToolResult(false, JsonSerializer.Serialize(response)));
    }
}
```

MCP does **only**: JSON arg parsing, calling the service, returning structured JSON.

### Layer 5 — Tool registry routing

```csharp
// src/<Project>.Mcp/Tools/ToolRegistry.cs
public Task<ToolResult> InvokeAsync(string toolName, JsonElement args, CancellationToken ct)
{
    if (_handlers.TryGetValue(toolName, out var handler))
        return handler.HandleAsync(args, ct);
    return _bridge.InvokeAsync(toolName, args, ct);   // fallback to auto-bridge
}
```

Handlers register via DI; the registry resolves them by tool name. Commands without an explicit handler keep using whatever bridge exists (in roslyn-tools, a text-capture path through the CLI). The pattern is **additive**: new commands get the full Core-service treatment, existing commands migrate one at a time.

## Tests, by layer

| Layer        | What to test                                        | Test cost  |
| ------------ | --------------------------------------------------- | ---------- |
| Service      | All logic. Edge cases. Property-based if relevant.  | Bulk       |
| CLI command  | Arg parsing maps to Request correctly. Output shape.| Thin       |
| MCP handler  | JSON args map to Request correctly. JSON return shape. | Thin    |
| Integration  | End-to-end: real CLI invocation, real MCP invocation. | Smoke   |

Service tests dominate. Host tests assert wiring only — if the service is right and the wiring is right, the host's job is done.

## Asymmetries that are OK

Hosts have their own adapter layer for a reason. These differences are **expected**, not leaks:

- **CLI-only**: format flags (`--json` / `--yaml` / `--markdown`), dry-run prompts, decisions YAML files, progress logging to stderr, colour rendering, feedback capture.
- **MCP-only**: tool name normalisation (`-` → `_`), JSON schemas for inputs, native structured returns, schema discovery via `tools/list`, no dry-run override (the MCP client handles approval UX).
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
3. **Add an MCP handler**. New file in `Handlers/`. Wraps the service.
4. **Wire the handler**. Register it in `McpServiceRegistration`; the registry routes around the auto-bridge for that tool name.
5. **Compare output**. CLI text + MCP JSON should both reflect the same Response. If they diverge unexpectedly, the divergence is the bug to fix.

Migration is one command per commit. The codebase tolerates a mixed state where some commands are migrated and others still go through the auto-bridge.

## Anti-patterns to avoid

- **God service**: cramming unrelated operations into one Core class. Split by feature, not by host.
- **Request DTO holds primitive grab-bag**: a Request with 12 nullable fields is a smell. Either split the operation, or extract sub-records.
- **Response DTO holds host-specific rendering hints**: `Color`, `IsHighlighted`, `PaddingLeft` — those are host concerns. Keep them out.
- **Service calls into host code via injected interface**: if the service depends on `IFooConsole` whose only implementation is in the CLI host, you've inverted the layering. The service should not know about hosts at all.

## Worked examples in roslyn-tools

- **`suggest` (new feature, all-in)** — `SuggestService` + `SuggestRequest`/`SuggestResponse` in Core; `SuggestCommand` in CLI; `SuggestHandler` in MCP. See `src/RoslynTools/Services/Suggest/` and the two adapter files.
- **`outline` (existing migration)** — CLI command was already a thin wrapper around `OutlineService`. Migration added `OutlineRequest` and a native `OutlineHandler`. The MCP tool now returns native JSON instead of text-captured CLI output.

When adding a new feature, follow this pattern from the first commit. When touching an existing command's logic, take the opportunity to extract it.
