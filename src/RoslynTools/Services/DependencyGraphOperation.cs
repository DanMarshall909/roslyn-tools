using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

internal sealed record DependencyGraphRequest(
    string? Solution = null,
    string? Type = null,
    int? Depth = null,
    string? Scope = null);

internal sealed record DependencyGraphEdge(string From, string To);
internal sealed record DependencyGraphResult(IReadOnlyList<string> Nodes, IReadOnlyList<DependencyGraphEdge> Edges);

internal sealed class DependencyGraphOperation
    : CommandApplicationPort<DependencyGraphRequest, DependencyGraphResult>, IRtService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IWorkspaceSessionFactory _sessions;
    private readonly DependencyGraphService _service;

    public DependencyGraphOperation(
        IWorkspaceSessionFactory sessions,
        DependencyGraphService service)
    {
        _sessions = sessions;
        _service = service;
    }

    public override ApplicationCommandDefinition Definition => DependencyGraphCommandDefinition.Definition;

    public override async Task<Result<DependencyGraphResult, RtError>> ExecuteAsync(
        DependencyGraphRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Depth < 0)
            return Result<DependencyGraphResult, RtError>.Err(
                new RtError(RtErrorKind.InvalidInput, "--depth must be >= 0."));

        try
        {
            var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
            using (workspace)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var json = await _service.BuildGraphAsync(
                    solution,
                    request.Type,
                    request.Depth,
                    "json",
                    request.Scope);
                var graph = JsonSerializer.Deserialize<GraphWire>(json, JsonOptions) ?? new();
                return Result<DependencyGraphResult, RtError>.Ok(new(
                    graph.Nodes,
                    graph.Edges.Select(static edge => new DependencyGraphEdge(edge.From, edge.To)).ToList()));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            return Result<DependencyGraphResult, RtError>.Err(
                new RtError(RtErrorKind.InvalidInput, ex.Message));
        }
    }

    private sealed class GraphWire
    {
        public List<string> Nodes { get; init; } = [];
        public List<EdgeWire> Edges { get; init; } = [];
    }

    private sealed record EdgeWire(string From, string To);
}

internal static class DependencyGraphCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "dependency-graph",
        false,
        true,
        "analyze",
        Aliases: ["depgraph"],
        WhenToUse:
        "Use this instead of Read when visualising the type-level dependency graph for a solution or a specific root type to understand coupling before a large refactor. Use --format dot to pipe into Graphviz, or --format json for programmatic consumption. Use --type + --depth to focus on a single type's fan-out.",
        Examples:
        [
            "rt analyze dependency-graph --solution C:\\code\\libraries\\ProjectX.Libraries.sln --scope ProjectX.Core",
            "rt analyze depgraph --type BookingService --depth 2 --solution RoslynTools.slnx",
            "rt analyze dependency-graph --format dot --solution RoslynTools.slnx | dot -Tsvg -o deps.svg"
        ],
        KnownIssues:
        [
            "Graph edges are built from fields, method parameters/returns, base types, and interfaces only — local variable type usages inside method bodies are missed. Static command classes with no fields appear as isolated nodes even when they reference WorkspaceLoader/SolutionWriter internally. See ai-improvements row #12."
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(DependencyGraphRequest),
        typeof(DependencyGraphResult),
        true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
