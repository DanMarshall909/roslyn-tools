using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;

namespace RoslynTools.Mcp.Handlers;

/// <summary>
///     Native MCP handler for the <c>outline</c> tool. Calls
///     <see cref="OutlineService" /> directly and returns structured JSON,
///     bypassing <see cref="CliCommandBridge" />'s text-capture path (which
///     would otherwise wrap the CLI's already-JSON output inside a string).
/// </summary>
internal sealed class OutlineHandler : IMcpHandler
{
    // Encoder is centralised in JsonEncoders.Select(RtContext.RelaxJson) so this handler
    // tracks the global --relax-json toggle.
    private static JsonSerializerOptions JsonOptions => JsonEncoders.Select(RtContext.RelaxJson);

    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly OutlineService _outline;

    public OutlineHandler(IWorkspaceSessionFactory sessionFactory, OutlineService outline)
    {
        _sessionFactory = sessionFactory;
        _outline = outline;
    }

    public string ToolName => "outline";

    public ToolDefinition Definition => new(
        ToolName,
        "List types and member signatures in a file, directory, glob, namespace, or by regex. "
        + "Returns structured JSON. Use summary=true for a compact one-entry-per-type view.",
        JsonSchemaBuilder.Object(
            required: ["solution"],
            properties: new Dictionary<string, Dictionary<string, object>>
            {
                ["target"] = JsonSchemaBuilder.String("File path, directory, glob, or namespace"),
                ["regex"] = JsonSchemaBuilder.String("Regex matched against solution file paths"),
                ["solution"] = JsonSchemaBuilder.String("Path to the .sln/.slnx file"),
                ["summary"] = JsonSchemaBuilder.Boolean("Compact mode: one entry per top-level type"),
                ["withLocations"] = JsonSchemaBuilder.Boolean(
                    "Legacy verbose mode: grouped members with per-member line ranges")
            }));

    public async Task<ToolResult> HandleAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var request = new OutlineRequest(
            Target: ReadString(arguments, "target"),
            Regex: ReadString(arguments, "regex"),
            Solution: ReadString(arguments, "solution"),
            Summary: ReadBool(arguments, "summary"),
            WithLocations: ReadBool(arguments, "withLocations"));

        if (string.IsNullOrEmpty(request.Solution))
            return new ToolResult(true, "Missing required property 'solution'.");

        var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            var result = await DispatchAsync(sln, request);
            var json = JsonSerializer.Serialize(result, JsonOptions);
            return new ToolResult(false, json);
        }
    }

    private async Task<List<object>> DispatchAsync(
        Microsoft.CodeAnalysis.Solution sln,
        OutlineRequest req)
    {
        // Dispatch mirrors the CLI command's logic in OutlineCommand.cs. Kept here
        // intentionally so OutlineService's public surface doesn't change — that
        // surface consolidation is a separate refactor.
        if (req.Regex is not null)
            return await _outline.OutlineRegexAsync(sln, req.Regex, req.Summary, req.WithLocations);
        if (req.Target is null)
            return [];
        if (req.Target.EndsWith(".*", StringComparison.Ordinal)
            && !req.Target.Contains('/')
            && !req.Target.Contains('\\'))
            return await _outline.OutlineNamespaceAsync(sln, req.Target, req.Summary, req.WithLocations);
        if (req.Target.Contains('*') || req.Target.Contains('?'))
            return await _outline.OutlineGlobAsync(sln, req.Target, req.Summary, req.WithLocations);
        if (Directory.Exists(req.Target))
            return await _outline.OutlineDirectoryAsync(sln, req.Target, req.Summary, req.WithLocations);
        if (req.Target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || File.Exists(req.Target))
            return await _outline.OutlineFileAsync(sln, req.Target, req.Summary, req.WithLocations);
        return await _outline.OutlineNamespaceAsync(sln, req.Target, req.Summary, req.WithLocations);
    }

    private static string? ReadString(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private static bool ReadBool(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var el)) return false;
        return el.ValueKind == JsonValueKind.True;
    }
}
