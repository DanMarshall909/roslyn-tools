using System.Text.Json;
using RoslynTools.Mcp.Handlers;
using RoslynTools.Mcp.Schemas;

namespace RoslynTools.Mcp.Tools;

internal sealed class ToolRegistry
{
    public ToolDefinition[] GetTools()
    {
        return new[]
        {
            CreateFindRefsToolDef(),
            CreateRenameToolDef(),
        };
    }

    public async Task<ToolResult> InvokeAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        return toolName switch
        {
            "find_refs" => await InvokeFindRefs(arguments, cancellationToken),
            "rename_symbol" => await InvokeRename(arguments, cancellationToken),
            _ => new ToolResult(true, $"Unknown tool: {toolName}")
        };
    }

    private ToolDefinition CreateFindRefsToolDef() => new(
        name: "find_refs",
        description: "Find all references to a symbol in a solution",
        inputSchema: JsonSchemaBuilder.Object(
            required: new[] { "symbol", "solution" },
            properties: new()
            {
                ["symbol"] = JsonSchemaBuilder.String("Full name or simple name of the symbol to find"),
                ["solution"] = JsonSchemaBuilder.String("Path to .sln or .slnx file"),
                ["scope"] = JsonSchemaBuilder.EnumSchema("public", "internal", "private", "all"),
                ["includeTests"] = JsonSchemaBuilder.Boolean("Include test projects"),
            }));

    private ToolDefinition CreateRenameToolDef() => new(
        name: "rename_symbol",
        description: "Rename a symbol across the solution (with dry-run safety)",
        inputSchema: JsonSchemaBuilder.Object(
            required: new[] { "oldName", "newName", "solution" },
            properties: new()
            {
                ["oldName"] = JsonSchemaBuilder.String("Current name of the symbol"),
                ["newName"] = JsonSchemaBuilder.String("New name for the symbol"),
                ["solution"] = JsonSchemaBuilder.String("Path to .sln or .slnx file"),
                ["scope"] = JsonSchemaBuilder.EnumSchema("public", "internal", "private", "all"),
                ["dryRun"] = JsonSchemaBuilder.Boolean("Preview changes without writing (default: true)"),
                ["returnDiff"] = JsonSchemaBuilder.Boolean("Include unified diff in response (default: true)"),
                ["apply"] = JsonSchemaBuilder.Boolean("Actually apply changes (default: false)"),
            }));

    private async Task<ToolResult> InvokeFindRefs(JsonElement args, CancellationToken ct)
    {
        var scope = args.TryGetProperty("scope", out var s) ? s.GetString() : "all";
        var req = new FindReferencesRequest(
            Symbol: args.GetProperty("symbol").GetString()!,
            Solution: args.GetProperty("solution").GetString()!,
            Scope: scope ?? "all",
            IncludeTests: args.TryGetProperty("includeTests", out var t) && t.GetBoolean());

        var handler = new FindReferencesHandler();
        var result = await handler.HandleAsync(req, ct);
        return new ToolResult(false, JsonSerializer.Serialize(result));
    }

    private async Task<ToolResult> InvokeRename(JsonElement args, CancellationToken ct)
    {
        var scope = args.TryGetProperty("scope", out var s) ? s.GetString() : "all";
        var req = new RenameSymbolRequest(
            OldName: args.GetProperty("oldName").GetString()!,
            NewName: args.GetProperty("newName").GetString()!,
            Solution: args.GetProperty("solution").GetString()!,
            Scope: scope ?? "all",
            DryRun: args.TryGetProperty("dryRun", out var dr) ? dr.GetBoolean() : true,
            ReturnDiff: args.TryGetProperty("returnDiff", out var rd) ? rd.GetBoolean() : true,
            Apply: args.TryGetProperty("apply", out var ap) && ap.GetBoolean());

        var handler = new RenameSymbolHandler();
        var result = await handler.HandleAsync(req, ct);
        return new ToolResult(false, JsonSerializer.Serialize(result));
    }
}

internal sealed record ToolDefinition(string name, string description, JsonSchema inputSchema);
internal sealed record ToolResult(bool isError, string content);

internal sealed class JsonSchema
{
    public required string Type { get; init; }
    public string? Description { get; init; }
    public Dictionary<string, JsonSchema>? Properties { get; init; }
    public string[]? Required { get; init; }
    public string[]? EnumValues { get; init; }
}

internal static class JsonSchemaBuilder
{
    public static JsonSchema Object(
        string[]? required = null,
        Dictionary<string, JsonSchema>? properties = null) =>
        new()
        {
            Type = "object",
            Required = required,
            Properties = properties ?? new(),
        };

    public static JsonSchema String(string description = "") =>
        new() { Type = "string", Description = description };

    public static JsonSchema Number(string description = "") =>
        new() { Type = "number", Description = description };

    public static JsonSchema Boolean(string description = "") =>
        new() { Type = "boolean", Description = description };

    public static JsonSchema EnumSchema(params string[] values) =>
        new() { Type = "string", EnumValues = values };
}
