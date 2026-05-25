using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;

namespace RoslynTools.Mcp.Handlers;

/// <summary>
///     Native MCP handler for the <c>find_refs</c> tool. Calls
///     <see cref="FindRefsService" /> directly and returns structured JSON with
///     each reference's file, line, and source text — instead of routing
///     through <see cref="CliCommandBridge" />'s text-capture path.
/// </summary>
internal sealed class FindReferencesHandler : IMcpHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IWorkspaceSessionFactory _workspaceLoader;
    private readonly FindRefsService _findRefs;

    public FindReferencesHandler(IWorkspaceSessionFactory workspaceLoader, FindRefsService findRefs)
    {
        _workspaceLoader = workspaceLoader;
        _findRefs = findRefs;
    }

    public string ToolName => "find_refs";

    public ToolDefinition Definition => new(
        ToolName,
        "Find all references to a symbol across the solution. "
        + "Returns structured JSON with the resolved symbol name and a list of "
        + "references (filePath, line, lineText).",
        JsonSchemaBuilder.Object(
            required: ["symbol"],
            properties: new Dictionary<string, Dictionary<string, object>>
            {
                ["symbol"] = JsonSchemaBuilder.String("Simple name of the symbol"),
                ["kind"] = JsonSchemaBuilder.String("Symbol kind filter: type, method, property, field, namespace"),
                ["solution"] = JsonSchemaBuilder.String("Path to the .sln/.slnx file")
            }));

    public async Task<ToolResult> HandleAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var symbol = ReadString(arguments, "symbol");
        if (string.IsNullOrEmpty(symbol))
            return new ToolResult(true, "Missing required string property 'symbol'.");

        var kind = ReadString(arguments, "kind");
        var solutionPath = ReadString(arguments, "solution");

        var sln = await _workspaceLoader.GetSolutionAsync(solutionPath);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _findRefs.FindAsync(sln, symbol, kind);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return new ToolResult(false, json);
    }

    private static string? ReadString(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrEmpty(s) ? null : s;
    }
}
