using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Mcp.Tools;
using RoslynTools.Models;
using RoslynTools.Services;

namespace RoslynTools.Mcp.Handlers;

/// <summary>
///     Native MCP handler for the <c>rename</c> tool. Calls
///     <see cref="RenameService" /> directly and returns structured JSON,
///     bypassing <see cref="CliCommandBridge" />'s text-capture path. Defaults
///     to a dry-run preview; callers must opt in with <c>dry-run: false</c>
///     to write changes to disk.
/// </summary>
internal sealed class RenameSymbolHandler : IMcpHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IWorkspaceSessionFactory _workspaceLoader;
    private readonly RenameService _rename;

    public RenameSymbolHandler(
        IWorkspaceSessionFactory workspaceLoader,
        RenameService rename)
    {
        _workspaceLoader = workspaceLoader;
        _rename = rename;
    }

    public string ToolName => "rename";

    public ToolDefinition Definition => new(
        ToolName,
        "Rename a symbol and all its references across the solution. "
        + "Defaults to a dry-run preview; pass dry-run: false to write changes. "
        + "Returns structured JSON with the changed-file count, the changed file paths, "
        + "and an applied flag indicating whether the rewrite was persisted.",
        JsonSchemaBuilder.Object(
            required: ["symbol", "new-name"],
            properties: new Dictionary<string, Dictionary<string, object>>
            {
                ["symbol"] = JsonSchemaBuilder.String("Fully-qualified symbol name to rename"),
                ["new-name"] = JsonSchemaBuilder.String("New name for the symbol"),
                ["kind"] = JsonSchemaBuilder.String("Symbol kind filter: type, method, property, field, namespace"),
                ["solution"] = JsonSchemaBuilder.String("Path to the .sln/.slnx file"),
                ["dry-run"] = JsonSchemaBuilder.Boolean("Preview without writing (default true)"),
                ["related-edits"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object> { ["type"] = "string" },
                    ["description"] = "Related edit IDs to apply. Omit to apply only the semantic rename; "
                                      + "IDs are returned in relatedRenamePlan."
                }
            }));

    public async Task<ToolResult> HandleAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var symbol = ReadString(arguments, "symbol");
        if (string.IsNullOrEmpty(symbol))
            return new ToolResult(true, "Missing required string property 'symbol'.");

        var newName = ReadString(arguments, "new-name");
        if (string.IsNullOrEmpty(newName))
            return new ToolResult(true, "Missing required string property 'new-name'.");

        var kind = ReadString(arguments, "kind");
        var solutionPath = ReadString(arguments, "solution");
        var dryRun = ReadBoolOrDefault(arguments, "dry-run", defaultValue: true);
        var relatedEditIds = ReadStringArray(arguments, "related-edits");

        var sln = await _workspaceLoader.GetSolutionAsync(solutionPath);
        cancellationToken.ThrowIfCancellationRequested();

        var selectedIds = relatedEditIds.ToHashSet(StringComparer.Ordinal);
        var result = await _rename.ExecuteAsync(
            sln,
            new RenameRequest(symbol, newName, kind, dryRun, selectedIds));
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

    private static bool ReadBoolOrDefault(JsonElement args, string name, bool defaultValue)
    {
        if (!args.TryGetProperty(name, out var el)) return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }

    private static string[] ReadStringArray(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array)
            return [];

        return element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
    }
}
