using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Mcp.Tools;

namespace RoslynTools.Mcp;

/// <summary>
/// Class for the server component of the Roslyn MCP system.
/// </summary>
internal sealed class Server(ToolRegistry toolRegistry, TextReader input, TextWriter output)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // JSON-RPC 2.0 requires the response `id` field to be present even
        // when its value is null (otherwise the client cannot correlate it
        // with the originating request). Do NOT enable WhenWritingNull here.
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly TextReader _input = input;
    private readonly TextWriter _output = output;

    private readonly ToolRegistry _toolRegistry = toolRegistry;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _input.ReadLineAsync();
            if (line == null)
                break;

            object? id = null;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var request = doc.RootElement;

                if (!request.TryGetProperty("jsonrpc", out _) ||
                    !request.TryGetProperty("method", out var methodProp) ||
                    !request.TryGetProperty("id", out var idProp))
                {
                    await SendErrorAsync(id, -32700, "Invalid Request");
                    continue;
                }

                // Materialize the JSON-RPC id into a CLR primitive BEFORE the
                // JsonDocument is disposed at the end of this `using` scope.
                // Embedding a JsonElement in the serialized response is what
                // crashes the process when its parent document goes away
                // (US-130 / FB-001).
                id = UnwrapJsonElement(idProp);
                var method = methodProp.GetString();

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
                await SendErrorAsync(id, -32603, $"Internal error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Materializes a JSON-RPC id <see cref="JsonElement"/> into a CLR
    /// primitive that is safe to embed in a response object after the source
    /// <see cref="JsonDocument"/> has been disposed. Per JSON-RPC 2.0 the id
    /// MUST be a string, number, or null — objects and arrays collapse to
    /// null (the spec treats them as invalid).
    /// </summary>
    private static object? UnwrapJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        // Undefined covers the default(JsonElement) case used when no id was
        // present on the inbound request — surface as JSON null.
        _ => null
    };

    private object HandleInitialize(object? id)
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

    private object HandleListTools(object? id)
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
        object? id,
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

        var toolResult = new Dictionary<string, object>
        {
            ["content"] = new[] { new { type = "text", text = result.content } },
            ["isError"] = result.isError
        };
        if (result.structuredContent is { } structuredContent)
            toolResult["structuredContent"] = structuredContent;

        return new
        {
            jsonrpc = "2.0",
            id,
            result = toolResult
        };
    }

    private static object CreateErrorResponse(object? id, int code, string message)
    {
        return new
        {
            jsonrpc = "2.0",
            id,
            error = new { code, message }
        };
    }

    private async Task SendErrorAsync(object? id, int code, string message)
    {
        await SendResponseAsync(CreateErrorResponse(id, code, message));
    }

    /// <summary>
    /// Internal seam exposed for unit testing the defence-in-depth catch in
    /// <see cref="SendResponseAsync(object)"/>. Production callers always go
    /// through the private path; the test-only entry point keeps the
    /// behaviour observable without weakening the public surface.
    /// </summary>
    internal Task SendResponseForTestAsync(object response) => SendResponseAsync(response);

    private async Task SendResponseAsync(object response)
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(response, SerializerOptions);
        }
        catch (Exception ex)
        {
            // Defence-in-depth (US-130): a fault inside Serialize must NOT
            // tear the server down. Emit a JSON-RPC -32603 envelope with
            // id=null and keep accepting requests.
            RtLog.Warning("MCP serialization failed: {0}", ex.Message);
            var fallback = new
            {
                jsonrpc = "2.0",
                id = (object?)null,
                error = new { code = -32603, message = $"Internal error: serialization failed: {ex.Message}" }
            };
            json = JsonSerializer.Serialize(fallback, SerializerOptions);
        }

        await _output.WriteLineAsync(json);
    }
}
