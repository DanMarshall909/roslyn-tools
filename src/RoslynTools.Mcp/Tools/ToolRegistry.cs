using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoslynTools.Mcp.Tools;

/// <summary>
///     Routes MCP tool invocations. Native handlers (those implementing
///     <see cref="IMcpHandler" />) take precedence over the auto-bridge —
///     commands without a dedicated handler fall through to
///     <see cref="CliCommandBridge" />, which executes the CLI and captures
///     stdout as text.
/// </summary>
/// <remarks>
///     Migration pattern: as commands gain native handlers (and richer JSON
///     returns), they shift from the bridge to <see cref="_handlers" />. The
///     bridge stays for the long tail.
/// </remarks>
internal sealed class ToolRegistry
{
    private readonly CliCommandBridge _bridge;
    private readonly Dictionary<string, IMcpHandler> _handlers;
    private readonly ToolDefinition[] _tools;

    public ToolRegistry(ToolSchemaBuilder schemaBuilder, CliCommandBridge bridge, IEnumerable<IMcpHandler> handlers)
    {
        _bridge = bridge;
        _handlers = handlers.ToDictionary(h => h.ToolName, StringComparer.Ordinal);

        // Native handler tool definitions override or extend the auto-bridged ones.
        var bridged = schemaBuilder.Build()
            .Where(t => !_handlers.ContainsKey(t.name))
            .ToList();
        var native = _handlers.Values.Select(h => h.Definition with
        {
            outputSchema = h.Definition.outputSchema ?? JsonSchemaBuilder.StructuredOutput()
        });
        _tools = bridged.Concat(native).ToArray();
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
        if (_handlers.TryGetValue(toolName, out var handler))
            return handler.HandleAsync(arguments, cancellationToken);
        return _bridge.InvokeAsync(toolName, arguments, cancellationToken);
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
    Dictionary<string, object>? outputSchema = null);

/// <summary>
/// Class representing the result of a tool execution in the Roslyn MCP system.
/// </summary>
internal sealed record ToolResult(bool isError, string content);

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

    public static Dictionary<string, object> String(string description = "")
    {
        return Scalar("string", description);
    }

    public static Dictionary<string, object> Boolean(string description = "")
    {
        return Scalar("boolean", description);
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

    private static Dictionary<string, object> Scalar(string type, string description)
    {
        var schema = new Dictionary<string, object> { ["type"] = type };
        if (!string.IsNullOrEmpty(description))
            schema["description"] = description;
        return schema;
    }
}
