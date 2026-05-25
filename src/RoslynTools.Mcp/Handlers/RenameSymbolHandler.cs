using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Mcp.Tools;
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
    private readonly ISolutionChangeWriter _solutionChangeWriter;

    public RenameSymbolHandler(
        IWorkspaceSessionFactory workspaceLoader,
        RenameService rename,
        ISolutionChangeWriter solutionChangeWriter)
    {
        _workspaceLoader = workspaceLoader;
        _rename = rename;
        _solutionChangeWriter = solutionChangeWriter;
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
                ["dry-run"] = JsonSchemaBuilder.Boolean("Preview without writing (default true)")
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

        var sln = await _workspaceLoader.GetSolutionAsync(solutionPath);
        cancellationToken.ThrowIfCancellationRequested();

        var newSln = await _rename.RenameAsync(sln, symbol, newName, kind);

        var changedDocs = newSln.GetChanges(sln)
            .GetProjectChanges()
            .SelectMany(p => p.GetChangedDocuments())
            .Select(id => newSln.GetDocument(id)?.FilePath)
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .ToList();

        if (!dryRun)
            await _solutionChangeWriter.ApplyChangesAsync(sln, newSln, false);

        var response = new
        {
            symbol,
            newName,
            changedFiles = changedDocs.Count,
            changedFilePaths = changedDocs,
            applied = !dryRun
        };

        var json = JsonSerializer.Serialize(response, JsonOptions);
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
}
