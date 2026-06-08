using System.Text.Json;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services.SemanticDiff;
using RoslynTools.Services.SemanticDiff.Git;

namespace RoslynTools.Mcp.Handlers;

/// <summary>
///     Native MCP handler for the <c>semantic_diff</c> tool. Thin adapter around the same
///     <see cref="GitSemanticDiffSourceProvider" /> version source and <see cref="SemanticDiffService" />
///     engine the CLI's <c>rt analyze semantic-diff</c> resolves from DI — no comparison logic lives
///     here. Loads the before/after snapshots from git (default: working-tree-vs-HEAD in the server's
///     current directory; <c>from</c>/<c>to</c> select other refs; <c>directory</c> targets another
///     repository), runs the engine, and returns the surfaced deltas as JSON.
/// </summary>
/// <remarks>
///     The JSON is produced by <see cref="SemanticDiffJson.Serialize" /> — the identical projection the
///     CLI's <c>--json</c> uses — so CLI and MCP output are byte-identical for the same diff. Repository
///     / ref validation errors (not-a-git-repository, invalid-ref) surface as an error
///     <see cref="ToolResult" /> rather than propagating out of <see cref="HandleAsync" />.
/// </remarks>
internal sealed class SemanticDiffHandler : IMcpHandler
{
    private readonly GitSemanticDiffSourceProvider _source;
    private readonly SemanticDiffService _engine;

    public SemanticDiffHandler(GitSemanticDiffSourceProvider source, SemanticDiffService engine)
    {
        _source = source;
        _engine = engine;
    }

    public string ToolName => "semantic_diff";

    public ToolDefinition Definition => new(
        ToolName,
        "Semantic C# diff over git: ignores comments and whitespace, treats a declaration moved to "
        + "another file as unchanged, and pairs a pure rename into one entry. Returns structured "
        + "per-declaration deltas. Defaults to comparing the working tree against HEAD.",
        JsonSchemaBuilder.Object(
            properties: new Dictionary<string, Dictionary<string, object>>
            {
                ["from"] = JsonSchemaBuilder.String("Git ref for the 'before' side. Omit to default to HEAD."),
                ["to"] = JsonSchemaBuilder.String("Git ref for the 'after' side. Omit to compare against the working tree."),
                ["ignoreAccessibility"] = JsonSchemaBuilder.Boolean(
                    "Treat declarations that differ only in accessibility modifiers as unchanged."),
                ["detectRenames"] = JsonSchemaBuilder.Boolean(
                    "Pair a pure rename into one Renamed entry instead of Added + Removed (default true)."),
                ["directory"] = JsonSchemaBuilder.String(
                    "Path to the git repository to diff. Omit to use the server's current directory.")
            }));

    public Task<ToolResult> HandleAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var from = ReadString(arguments, "from");
        var to = ReadString(arguments, "to");
        var directory = ReadString(arguments, "directory");
        var ignoreAccessibility = ReadBool(arguments, "ignoreAccessibility") ?? false;
        var detectRenames = ReadBool(arguments, "detectRenames") ?? true;

        // A directory arg targets a specific repository; otherwise reuse the injected cwd-bound
        // provider. Either way the provider owns the HEAD-default and working-tree-vs-ref switch.
        var source = directory is null
            ? _source
            : new GitSemanticDiffSourceProvider(new GitClient(directory));

        try
        {
            var (before, after) = source.Load(from, to);
            var options = new SemanticDiffOptions(IgnoreAccessibility: ignoreAccessibility, DetectRenames: detectRenames);
            var result = _engine.Compare(new SemanticDiffRequest(before, after, options));

            var json = SemanticDiffJson.Serialize(result.Deltas);
            return Task.FromResult(new ToolResult(false, json));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Repository / ref validation failures (not a git repo, invalid ref) are routine
            // user errors — surface as an error result, not an unhandled exception.
            return Task.FromResult(new ToolResult(true, ex.Message));
        }
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static bool? ReadBool(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
