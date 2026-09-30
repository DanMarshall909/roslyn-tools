using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynTools.Mcp.Tools;
using ProtocolToolAnnotations = ModelContextProtocol.Protocol.ToolAnnotations;

namespace RoslynTools.Mcp;

/// <summary>
/// Projects the shared command catalogue through the official MCP protocol host.
/// </summary>
internal sealed class Server(ToolRegistry toolRegistry, Stream input, Stream output)
{
    private const int ToolPageSize = 20;
    private const string ToolCursorPrefix = "rt-tools-v1";
    private readonly ToolRegistry _toolRegistry = toolRegistry;
    private readonly Stream _input = input;
    private readonly Stream _output = output;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = "roslyn-tools-mcp",
                Version = "1.0.0"
            },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (request, _) =>
                    ValueTask.FromResult(ListTools(request.Params?.Cursor)),
                CallToolHandler = async (request, requestCancellationToken) =>
                {
                    var arguments = JsonSerializer.SerializeToElement(
                        request.Params.Arguments ?? new Dictionary<string, JsonElement>());
                    var result = await _toolRegistry.InvokeAsync(
                        request.Params.Name,
                        arguments,
                        requestCancellationToken);
                    return new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = result.content }],
                        IsError = result.isError,
                        StructuredContent = result.structuredContent
                    };
                }
            }
        };

        await using var transport = new StreamServerTransport(
            _input,
            _output,
            "roslyn-tools-mcp");
        await using var server = McpServer.Create(transport, options);
        await server.RunAsync(cancellationToken);
    }

    private ListToolsResult ListTools(string? cursor)
    {
        var definitions = _toolRegistry.GetTools()
            .OrderBy(definition => DiscoveryPriority(definition.name))
            .ToArray();
        var start = ParseToolCursor(cursor, definitions.Length);
        var page = definitions.Skip(start).Take(ToolPageSize)
            .Select(ToProtocolTool).ToArray();
        var next = start + page.Length;
        return new ListToolsResult
        {
            Tools = page,
            NextCursor = next < definitions.Length
                ? $"{ToolCursorPrefix}:{definitions.Length.ToString(CultureInfo.InvariantCulture)}:{next.ToString(CultureInfo.InvariantCulture)}"
                : null
        };
    }

    private static int ParseToolCursor(string? cursor, int count)
    {
        if (cursor is null)
            return 0;

        var parts = cursor.Split(':');
        if (parts.Length != 3
            || parts[0] != ToolCursorPrefix
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var cursorCount)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var start)
            || cursorCount != count
            || start < ToolPageSize
            || start >= count
            || start % ToolPageSize != 0)
            throw new McpProtocolException("Invalid or stale tools/list cursor.", McpErrorCode.InvalidParams);

        return start;
    }

    private static int DiscoveryPriority(string name) => name switch
    {
        "prime" => 0,
        "suggest_command" => 1,
        "search_symbols" => 2,
        "relationships" => 3,
        "move_slice" => 4,
        _ => 5
    };

    private static Tool ToProtocolTool(ToolDefinition definition)
    {
        return new Tool
        {
            Name = definition.name,
            Description = definition.description,
            InputSchema = JsonSerializer.SerializeToElement(definition.inputSchema),
            OutputSchema = definition.outputSchema is null
                ? null
                : JsonSerializer.SerializeToElement(definition.outputSchema),
            Annotations = new ProtocolToolAnnotations
            {
                Title = definition.annotations.title,
                ReadOnlyHint = definition.annotations.readOnlyHint,
                DestructiveHint = definition.annotations.destructiveHint,
                IdempotentHint = definition.annotations.idempotentHint,
                OpenWorldHint = definition.annotations.openWorldHint
            },
            Meta = JsonSerializer.SerializeToNode(definition.metadata)?.AsObject()
        };
    }
}
