using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynTools.Mcp.Tools;

namespace RoslynTools.Mcp;

/// <summary>
/// Class for the server component of the Roslyn MCP system.
/// </summary>
internal sealed class Server
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly TextReader _input;
    private readonly TextWriter _output;

    private readonly ToolRegistry _toolRegistry;

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

            JsonElement idElement = default;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var request = doc.RootElement;

                if (!request.TryGetProperty("jsonrpc", out _) ||
                    !request.TryGetProperty("method", out var methodProp) ||
                    !request.TryGetProperty("id", out var idProp))
                {
                    await SendErrorAsync(idElement, -32700, "Invalid Request");
                    continue;
                }

                idElement = idProp.Clone();
                var method = methodProp.GetString();

                var response = method switch
                {
                    "initialize" => HandleInitialize(idElement),
                    "tools/list" => HandleListTools(idElement),
                    "tools/call" => await HandleCallToolAsync(request, idElement, cancellationToken),
                    _ => CreateErrorResponse(idElement, -32601, $"Method not found: {method}")
                };

                await SendResponseAsync(response);
            }
            catch (Exception ex)
            {
                await SendErrorAsync(idElement, -32603, $"Internal error: {ex.Message}");
            }
        }
    }

    private object HandleInitialize(JsonElement id)
    {
        return new
        {
            jsonrpc = "2.0",
            id,
            result = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { tools = new { } },
                serverInfo = new { name = "roslyn-tools-mcp", version = "1.0.0" }
            }
        };
    }

    private object HandleListTools(JsonElement id)
    {
        return new
        {
            jsonrpc = "2.0",
            id,
            result = new { tools = _toolRegistry.GetTools() }
        };
    }

    private async Task<object> HandleCallToolAsync(
        JsonElement request,
        JsonElement id,
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

        return new
        {
            jsonrpc = "2.0",
            id,
            result = new
            {
                content = new[] { new { type = "text", text = result.content } },
                result.isError
            }
        };
    }

    private static object CreateErrorResponse(JsonElement id, int code, string message)
    {
        return new
        {
            jsonrpc = "2.0",
            id,
            error = new { code, message }
        };
    }

    private async Task SendErrorAsync(JsonElement id, int code, string message)
    {
        await SendResponseAsync(CreateErrorResponse(id, code, message));
    }

    private async Task SendResponseAsync(object response)
    {
        var json = JsonSerializer.Serialize(response, SerializerOptions);
        await _output.WriteLineAsync(json);
    }
}
