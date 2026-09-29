using System.Text.Json;
using System.Text.Json.Nodes;
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
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult
                {
                    Tools = _toolRegistry.GetTools().Select(ToProtocolTool).ToArray()
                }),
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
