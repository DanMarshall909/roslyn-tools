using System.Text.Json;

namespace RoslynTools.Mcp.Tools;

internal sealed class ToolRegistry
{
    private readonly CliCommandBridge _bridge;
    private readonly ToolDefinition[] _tools;

    public ToolRegistry(ToolSchemaBuilder schemaBuilder, CliCommandBridge bridge)
    {
        _bridge = bridge;
        _tools = schemaBuilder.Build().ToArray();
    }

    public ToolDefinition[] GetTools()
    {
        return _tools;
    }

    public Task<ToolResult> InvokeAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        return _bridge.InvokeAsync(toolName, arguments, cancellationToken);
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
    {
        return Scalar("string", description);
    }

    public static Dictionary<string, object> Boolean(string description = "")
    {
        return Scalar("boolean", description);
    }

    private static Dictionary<string, object> Scalar(string type, string description)
    {
        var schema = new Dictionary<string, object> { ["type"] = type };
        if (!string.IsNullOrEmpty(description))
            schema["description"] = description;
        return schema;
    }
}
