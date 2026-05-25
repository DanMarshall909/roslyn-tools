using System.CommandLine;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Generates a dependency graph for a solution.
/// </summary>
internal sealed class DependencyGraphCommand : IRtCommand, IStructuredOutputCommand, IAnalyzeCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly DependencyGraphService _dependencyGraph;
    public DependencyGraphCommand(IWorkspaceSessionFactory sessionFactory, DependencyGraphService dependencyGraph)
    {
        _dependencyGraph = dependencyGraph;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "dependency-graph",
        false,
        true,
        "analyze",
        Aliases: ["depgraph"],
        WhenToUse:
        "Visualising the type-level dependency graph for a solution or a specific root type, to understand coupling before a large refactor. Use --format dot to pipe into Graphviz, or --format json for programmatic consumption. Use --type + --depth to focus on a single type's fan-out.",
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
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]
    );

    public Command Build()
    {
        var typeOpt = new Option<string?>("--type", "Root type to start graph from (optional)");
        var depthOpt = new Option<int?>("--depth", "Maximum traversal depth (optional)");
        var formatOpt = new Option<string?>("--format", "Output format: json (default) or dot");
        var scopeOpt = new Option<string?>("--scope", "Optional namespace prefix to limit analysis");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");

        var cmd = new Command("dependency-graph", "Build a type-level dependency graph for the solution");
        cmd.AddAlias("depgraph");
        cmd.AddAll(typeOpt, depthOpt, formatOpt, scopeOpt, solutionOpt);

        cmd.SetHandler(async ctx =>
        {
            var typeName = ctx.ParseResult.GetValueForOption(typeOpt);
            var depth = ctx.ParseResult.GetValueForOption(depthOpt);
            var format = ctx.ParseResult.GetValueForOption(formatOpt);
            var scope = ctx.ParseResult.GetValueForOption(scopeOpt);
            var solution = ctx.ParseResult.GetValueForOption(solutionOpt);

            var sln = await _sessionFactory.GetSolutionAsync(solution);
            var result = await _dependencyGraph
                .BuildGraphAsync(sln, typeName, depth, format ?? "json", scope);

            RtResultRenderer.WriteText(RtResult<string>.Ok(result));
        });

        return cmd;
    }

    /// <summary>
    ///     Builds a type-level dependency graph and returns it in the requested format.
    ///     Delegates to <see cref="DependencyGraphService.BuildGraphAsync" />.
    /// </summary>
    internal Task<string> BuildGraphAsync(
        Solution sln,
        string? typeName,
        int? depth,
        string format,
        string? scope)
    {
        return _dependencyGraph.BuildGraphAsync(sln, typeName, depth, format, scope);
    }
}
