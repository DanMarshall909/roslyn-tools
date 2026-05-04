using System.Text.Json;
using RoslynTools.Mcp.Tools;

namespace RoslynTools.Mcp;

internal sealed class Server
{
    private readonly ToolRegistry _toolRegistry;
    private readonly TextReader _input;
    private readonly TextWriter _output;

    public Server(ToolRegistry toolRegistry, TextReader input, TextWriter output)
    {
        _toolRegistry = toolRegistry;
        _input = input;
        _output = output;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _input.ReadLineAsync();
            if (line == null)
                break;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var request = doc.RootElement;

                if (!request.TryGetProperty("jsonrpc", out _) ||
                    !request.TryGetProperty("method", out var methodProp) ||
                    !request.TryGetProperty("id", out var idProp))
                {
                    await SendErrorAsync(-32700, "Invalid Request");
                    continue;
                }

                var method = methodProp.GetString();
                var id = idProp.GetInt32();

                var response = method switch
                {
                    "initialize" => HandleInitialize(id),
                    "tools/list" => HandleListTools(id),
                    "tools/call" => await HandleCallToolAsync(request, id, cancellationToken),
                    _ => CreateErrorResponse(id, -32601, $"Method not found: {method}")
                };

                await SendResponseAsync(response);
            }
            catch (Exception ex)
            {
                await SendErrorAsync(-32603, $"Internal error: {ex.Message}");
            }
        }
    }

    private object HandleInitialize(int id)
    {
        return new
        {
            jsonrpc = "2.0",
            id = id,
            result = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new
                {
                    tools = new object()
                },
                serverInfo = new
                {
                    name = "roslyn-tools-mcp",
                    version = "1.0.0"
                }
            }
        };
    }

    private object HandleListTools(int id)
    {
        var tools = _toolRegistry.GetTools();
        var toolObjects = tools.Select(tool => new
        {
            name = tool.name,
            description = tool.description,
            inputSchema = tool.inputSchema
        }).ToArray();

        return new
        {
            jsonrpc = "2.0",
            id = id,
            result = new
            {
                tools = toolObjects
            }
        };
    }

    private async Task<object> HandleCallToolAsync(
        JsonElement request,
        int id,
        CancellationToken cancellationToken)
    {
        if (!request.TryGetProperty("params", out var paramsProp))
            return CreateErrorResponse(id, -32602, "Missing params");

        if (!paramsProp.TryGetProperty("name", out var nameProp))
            return CreateErrorResponse(id, -32602, "Missing tool name");

        var toolName = nameProp.GetString();
        if (string.IsNullOrEmpty(toolName))
            return CreateErrorResponse(id, -32602, "Empty tool name");

        if (!paramsProp.TryGetProperty("arguments", out var argsProp))
            return CreateErrorResponse(id, -32602, "Missing arguments");

        var result = await _toolRegistry.InvokeAsync(toolName, argsProp, cancellationToken);

        if (result.isError)
        {
            return new
            {
                jsonrpc = "2.0",
                id = id,
                result = new
                {
                    type = "text",
                    text = $"Error: {result.content}"
                }
            };
        }

        return new
        {
            jsonrpc = "2.0",
            id = id,
            result = new
            {
                type = "text",
                text = result.content
            }
        };
    }

    private object CreateErrorResponse(int id, int code, string message)
    {
        return new
        {
            jsonrpc = "2.0",
            id = id,
            error = new
            {
                code = code,
                message = message
            }
        };
    }

    private async Task SendErrorAsync(int code, string message)
    {
        var error = new
        {
            jsonrpc = "2.0",
            error = new
            {
                code = code,
                message = message
            }
        };
        await SendResponseAsync(error);
    }

    private async Task SendResponseAsync(object response)
    {
        var json = JsonSerializer.Serialize(response);
        await _output.WriteLineAsync(json);
    }
}
