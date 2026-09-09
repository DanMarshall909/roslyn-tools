using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Mcp.Tools;

namespace RoslynTools.Mcp;

/// <summary>
/// Class for the server component of the Roslyn MCP system.
/// </summary>
internal sealed class Server(
    ToolRegistry toolRegistry,
    TextReader input,
    TextWriter output,
    McpRepositoryTrustApproval approval)
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
    private Task<string?>? _pendingRead;
    private bool _initializationReceived;
    private bool _initializationComplete;
    private bool _supportsFormElicitation;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await ReadLineAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            if (line == null)
                break;

            object? id = null;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var request = doc.RootElement;

                if (IsNotification(request))
                {
                    HandleNotification(request);
                    continue;
                }

                // A late or unsolicited client response cannot establish authority.
                if (IsResponse(request))
                    continue;

                if (request.ValueKind != JsonValueKind.Object ||
                    !request.TryGetProperty("jsonrpc", out _) ||
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
                    "initialize" => HandleInitialize(request, id),
                    "ping" => new { jsonrpc = "2.0", id, result = new { } },
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
        JsonValueKind.Number => element.TryGetInt64(out var l) ? (object)l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        // Undefined covers the default(JsonElement) case used when no id was
        // present on the inbound request — surface as JSON null.
        _ => null
    };

    private object HandleInitialize(JsonElement request, object? id)
    {
        if (_initializationReceived)
            return CreateErrorResponse(id, -32600, "The connection is already initialized.");

        _initializationReceived = true;
        var version = "2024-11-05";
        if (request.TryGetProperty("params", out var parameters)
            && parameters.ValueKind == JsonValueKind.Object)
        {
            if (parameters.TryGetProperty("protocolVersion", out var requestedVersion)
                && requestedVersion.ValueKind == JsonValueKind.String)
            {
                version = requestedVersion.GetString() switch
                {
                    "2024-11-05" => "2024-11-05",
                    "2025-03-26" => "2025-03-26",
                    "2025-06-18" => "2025-06-18",
                    _ => "2025-11-25"
                };
            }

            _supportsFormElicitation = version is "2025-06-18" or "2025-11-25"
                                      && parameters.TryGetProperty("capabilities", out var capabilities)
                                      && capabilities.ValueKind == JsonValueKind.Object
                                      && capabilities.TryGetProperty("elicitation", out var elicitation)
                                      && elicitation.ValueKind == JsonValueKind.Object
                                      && (!elicitation.EnumerateObject().Any()
                                          || elicitation.TryGetProperty("form", out var form)
                                          && form.ValueKind == JsonValueKind.Object);
        }

        return new
        {
            jsonrpc = "2.0",
            id,
            result = new
            {
                protocolVersion = version,
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

        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var approvalScope = approval.EnterRequestScope((root, token) =>
            RequestRepositoryApprovalAsync(root, id, requestCancellation, token));
        var result = await _toolRegistry.InvokeAsync(toolName, argsProp, requestCancellation.Token);

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

    private async Task<bool> RequestRepositoryApprovalAsync(
        string canonicalRoot,
        object? originatingRequestId,
        CancellationTokenSource requestCancellation,
        CancellationToken cancellationToken)
    {
        if (!_initializationComplete || !_supportsFormElicitation || cancellationToken.IsCancellationRequested)
            return false;

        var elicitationId = $"rt-trust-{Guid.NewGuid():N}";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(approval.Timeout);
        await SendResponseAsync(new
        {
            jsonrpc = "2.0",
            id = elicitationId,
            method = "elicitation/create",
            @params = new
            {
                message = $"Trust the repository root '{canonicalRoot}'? Loading or evaluating its projects can execute repository-controlled build scripts and other code with this process's permissions. This approval is saved for future interactive use. Approve only if you have reviewed and trust this exact root.",
                requestedSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        confirm = new
                        {
                            type = "boolean",
                            title = "Approve repository code execution",
                            description = $"I approve build evaluation for '{canonicalRoot}'.",
                            @default = false
                        }
                    },
                    required = new[] { "confirm" }
                }
            }
        });

        try
        {
            while (!timeout.IsCancellationRequested)
            {
                var line = await ReadLineAsync(timeout.Token);
                if (line is null)
                    return false;

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(line);
                }
                catch (JsonException)
                {
                    await SendErrorAsync(null, -32700, "Invalid JSON while awaiting user approval.");
                    continue;
                }

                using (document)
                {
                    var message = document.RootElement;
                    if (IsNotification(message))
                    {
                        if (IsCancellationFor(message, originatingRequestId, elicitationId))
                        {
                            requestCancellation.Cancel();
                            await CancelElicitationAsync(elicitationId);
                            return false;
                        }

                        HandleNotification(message);
                        continue;
                    }

                    if (IsResponse(message))
                    {
                        if (!message.TryGetProperty("id", out var responseId)
                            || responseId.ValueKind != JsonValueKind.String
                            || responseId.GetString() != elicitationId)
                            continue;

                        return !timeout.IsCancellationRequested && IsApprovalResponse(message);
                    }

                    if (message.ValueKind == JsonValueKind.Object
                        && message.TryGetProperty("id", out var otherId)
                        && message.TryGetProperty("method", out var otherMethod))
                    {
                        var id = UnwrapJsonElement(otherId);
                        if (otherMethod.ValueKind == JsonValueKind.String && otherMethod.GetString() == "ping")
                            await SendResponseAsync(new { jsonrpc = "2.0", id, result = new { } });
                        else
                            await SendErrorAsync(id, -32000, "A repository trust approval is pending. Retry after the originating tool call completes.");
                    }
                    else
                    {
                        await SendErrorAsync(null, -32700, "Invalid request while awaiting user approval.");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            // Denial leaves a pending read attached to this connection for the main loop.
        }

        await CancelElicitationAsync(elicitationId);
        return false;
    }

    private Task CancelElicitationAsync(string elicitationId) => SendResponseAsync(new
    {
        jsonrpc = "2.0",
        method = "notifications/cancelled",
        @params = new { requestId = elicitationId, reason = "Repository trust approval was canceled or expired." }
    });

    private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        // TextReader implementations may ignore cancellation. Keep the same read alive
        // after an approval timeout so it cannot consume and lose the next client message.
        _pendingRead ??= _input.ReadLineAsync();
        var line = await _pendingRead.WaitAsync(cancellationToken);
        _pendingRead = null;
        return line;
    }

    private void HandleNotification(JsonElement notification)
    {
        if (_initializationReceived
            && notification.GetProperty("method").GetString() == "notifications/initialized")
            _initializationComplete = true;
    }

    private static bool IsNotification(JsonElement message) =>
        message.ValueKind == JsonValueKind.Object
        && message.TryGetProperty("jsonrpc", out var version)
        && version.ValueKind == JsonValueKind.String && version.GetString() == "2.0"
        && !message.TryGetProperty("id", out _)
        && message.TryGetProperty("method", out var method)
        && method.ValueKind == JsonValueKind.String
        && method.GetString()!.StartsWith("notifications/", StringComparison.Ordinal);

    private static bool IsResponse(JsonElement message) =>
        message.ValueKind == JsonValueKind.Object
        && !message.TryGetProperty("method", out _)
        && message.TryGetProperty("id", out _)
        && (message.TryGetProperty("result", out _) || message.TryGetProperty("error", out _));

    private static bool IsApprovalResponse(JsonElement message) =>
        message.TryGetProperty("jsonrpc", out var version)
        && version.ValueKind == JsonValueKind.String && version.GetString() == "2.0"
        && !message.TryGetProperty("error", out _)
        && message.TryGetProperty("result", out var result)
        && result.ValueKind == JsonValueKind.Object
        && result.TryGetProperty("action", out var action)
        && action.ValueKind == JsonValueKind.String && action.GetString() == "accept"
        && result.TryGetProperty("content", out var content)
        && content.ValueKind == JsonValueKind.Object
        && content.TryGetProperty("confirm", out var confirm)
        && confirm.ValueKind == JsonValueKind.True;

    private static bool IsCancellationFor(JsonElement message, object? originatingId, string elicitationId)
    {
        if (message.GetProperty("method").GetString() != "notifications/cancelled"
            || !message.TryGetProperty("params", out var parameters)
            || parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("requestId", out var requestId))
            return false;

        var id = UnwrapJsonElement(requestId);
        return Equals(id, originatingId) || Equals(id, elicitationId);
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
        await _output.FlushAsync();
    }
}
