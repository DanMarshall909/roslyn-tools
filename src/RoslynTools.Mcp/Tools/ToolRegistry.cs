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

    private static ToolDefinition CreateFindRefsToolDef() => new(
        name: "find_refs",
        description: "Find all references to a symbol in a solution",
        inputSchema: JsonSchemaBuilder.Object(
            required: new[] { "symbol", "solution" },
            properties: new()
            {
                ["symbol"] = JsonSchemaBuilder.String("Full name or simple name of the symbol to find"),
                ["solution"] = JsonSchemaBuilder.String("Path to .sln or .slnx file"),
            }));

    private static ToolDefinition CreateRenameToolDef() => new(
        name: "rename_symbol",
        description: "Rename a symbol across the solution (with dry-run safety)",
        inputSchema: JsonSchemaBuilder.Object(
            required: new[] { "oldName", "newName", "solution" },
            properties: new()
            {
                ["oldName"] = JsonSchemaBuilder.String("Current name of the symbol"),
                ["newName"] = JsonSchemaBuilder.String("New name for the symbol"),
                ["solution"] = JsonSchemaBuilder.String("Path to .sln or .slnx file"),
                ["apply"] = JsonSchemaBuilder.Boolean("Write changes to disk (default: false — dry-run)"),
            }));

    private async Task<ToolResult> InvokeFindRefs(JsonElement args, CancellationToken ct)
    {
        var req = new FindReferencesRequest(
            Symbol: args.GetProperty("symbol").GetString()!,
            Solution: args.GetProperty("solution").GetString()!);

        var handler = new FindReferencesHandler();
        var result = await handler.HandleAsync(req, ct);
        return new ToolResult(false, JsonSerializer.Serialize(result));
    }

    private async Task<ToolResult> InvokeRename(JsonElement args, CancellationToken ct)
    {
        var req = new RenameSymbolRequest(
            OldName: args.GetProperty("oldName").GetString()!,
            NewName: args.GetProperty("newName").GetString()!,
            Solution: args.GetProperty("solution").GetString()!,
            Apply: args.TryGetProperty("apply", out var ap) && ap.GetBoolean());

        var handler = new RenameSymbolHandler();
        var result = await handler.HandleAsync(req, ct);
        return new ToolResult(false, JsonSerializer.Serialize(result));
    }
}

internal sealed record ToolDefinition(string name, string description, Dictionary<string, object> inputSchema);
internal sealed record ToolResult(bool isError, string content);

internal static class JsonSchemaBuilder
{
    public static Dictionary<string, object> Object(
        string[]? required = null,
        Dictionary<string, Dictionary<string, object>>? properties = null)
    {
        var schema = new Dictionary<string, object> { ["type"] = "object" };
        if (properties is { Count: > 0 })
            schema["properties"] = properties;
        if (required is { Length: > 0 })
            schema["required"] = required;
        return schema;
    }

    public static Dictionary<string, object> String(string description = "")
        => Scalar("string", description);

    public static Dictionary<string, object> Boolean(string description = "")
        => Scalar("boolean", description);

    private static Dictionary<string, object> Scalar(string type, string description)
    {
        var schema = new Dictionary<string, object> { ["type"] = type };
        if (!string.IsNullOrEmpty(description))
            schema["description"] = description;
        return schema;
    }
}
