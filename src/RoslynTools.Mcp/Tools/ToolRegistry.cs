using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoslynTools.Mcp.Tools;

/// <summary>
///     Routes MCP tool invocations exclusively through command descriptors.
/// </summary>
/// <remarks>
///     Commands gain an <see cref="Core.ICommandApplicationPort" /> as they migrate away
///     from the compatibility bridge; no command-specific MCP dispatch path exists.
/// </remarks>
internal sealed class ToolRegistry
{
    private readonly ToolSchemaBuilder _schemaBuilder;
    private readonly McpCommandAdapter _adapter;

    public ToolRegistry(
        ToolSchemaBuilder schemaBuilder,
        McpCommandAdapter adapter)
    {
        _schemaBuilder = schemaBuilder;
        _adapter = adapter;
    }

    public ToolDefinition[] GetTools()
    {
        return [.. _schemaBuilder.Build()];
    }

    public Task<ToolResult> InvokeAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        return _adapter.InvokeAsync(toolName, arguments, cancellationToken);
    }
}

/// <summary>
/// Class defining the structure of a tool in the Roslyn MCP system.
/// </summary>
internal sealed record ToolDefinition(
    string name,
    string description,
    Dictionary<string, object> inputSchema,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Dictionary<string, object>? outputSchema,
    ToolAnnotations annotations,
    [property: JsonPropertyName("_meta")] Dictionary<string, object> metadata);

internal sealed record ToolAnnotations(
    string title,
    bool readOnlyHint,
    bool destructiveHint,
    bool idempotentHint,
    bool openWorldHint);

/// <summary>
/// Class representing the result of a tool execution in the Roslyn MCP system.
/// </summary>
internal sealed record ToolResult(
    bool isError,
    string content,
    JsonElement? structuredContent = null)
{
    internal static ToolResult Structured(string json)
    {
        using var document = JsonDocument.Parse(json);
        return new ToolResult(false, json, document.RootElement.Clone());
    }
}

/// <summary>
/// Class for building JSON schemas in the Roslyn MCP system.
/// </summary>
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

    public static Dictionary<string, object> StructuredOutput()
    {
        return new Dictionary<string, object>
        {
            ["oneOf"] = new object[]
            {
                new Dictionary<string, object> { ["type"] = "object" },
                new Dictionary<string, object> { ["type"] = "array" }
            }
        };
    }
}
