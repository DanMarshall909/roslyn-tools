using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;

namespace RoslynTools.Mcp.Handlers;

/// <summary>
///     Native MCP handler for the <c>describe</c> tool. Calls <see cref="InspectService" />
///     directly and returns the symbol inspection as structured JSON — instead of routing
///     through <see cref="CliCommandBridge" />'s text-capture path. A routine not-found /
///     ambiguous lookup surfaces as an error <see cref="ToolResult" /> rather than an exception.
/// </summary>
internal sealed class DescribeHandler : IMcpHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IWorkspaceSessionFactory _workspaceLoader;
    private readonly InspectService _inspect;

    public DescribeHandler(IWorkspaceSessionFactory workspaceLoader, InspectService inspect)
    {
        _workspaceLoader = workspaceLoader;
        _inspect = inspect;
    }

    public string ToolName => "describe";

    public ToolDefinition Definition => new(
        ToolName,
        "Describe a symbol's shape — members, base types, interfaces, constructors, locations — "
        + "as structured JSON, without opening the file.",
        JsonSchemaBuilder.Object(
            required: ["symbol"],
            properties: new Dictionary<string, Dictionary<string, object>>
            {
                ["symbol"] = JsonSchemaBuilder.String("Fully-qualified symbol name to inspect"),
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

        var inspection = await _inspect.Describe(sln, symbol, kind);
        return inspection.Match(
            result => new ToolResult(false, JsonSerializer.Serialize(result, JsonOptions)),
            error => new ToolResult(true, error.Message));
    }

    private static string? ReadString(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrEmpty(s) ? null : s;
    }
}
