using System.Text.Json;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services.Suggest;

namespace RoslynTools.Mcp.Handlers;

/// <summary>
///     Native MCP handler for the <c>suggest_command</c> tool. Thin adapter
///     around <see cref="SuggestService" /> — parses MCP args, calls the service,
///     returns the structured response as JSON.
/// </summary>
/// <remarks>
///     Reference implementation of the Core-service + thin-host-adapter pattern.
///     The CLI's <c>SuggestCommand</c> and this handler share the same
///     <see cref="SuggestService" />; nothing about the matching logic lives in
///     either host.
/// </remarks>
internal sealed class SuggestHandler : IMcpHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly SuggestService _suggest;

    public SuggestHandler(SuggestService suggest)
    {
        _suggest = suggest;
    }

    public string ToolName => "suggest_command";

    public ToolDefinition Definition => new(
        ToolName,
        "Find the right rt command by describing what you want to do in natural language. "
        + "Returns ranked candidates with name, score, when-to-use, and example invocation.",
        JsonSchemaBuilder.Object(
            required: ["query"],
            properties: new Dictionary<string, Dictionary<string, object>>
            {
                ["query"] = JsonSchemaBuilder.String("Natural-language description of the task"),
                ["top"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Maximum candidates to return (default 3)" }
            }));

    public Task<ToolResult> HandleAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!arguments.TryGetProperty("query", out var queryEl) || queryEl.ValueKind != JsonValueKind.String)
            return Task.FromResult(new ToolResult(true, "Missing required string property 'query'."));

        var query = queryEl.GetString() ?? "";
        var top = 3;
        if (arguments.TryGetProperty("top", out var topEl) && topEl.ValueKind == JsonValueKind.Number)
            top = topEl.GetInt32();

        var response = _suggest.Suggest(new SuggestRequest(query, top));
        var json = JsonSerializer.Serialize(response, JsonOptions);
        return Task.FromResult(new ToolResult(false, json));
    }
}
