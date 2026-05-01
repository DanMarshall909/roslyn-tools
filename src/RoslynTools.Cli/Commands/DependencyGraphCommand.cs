using System.CommandLine;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class DependencyGraphCommand : IRtCommand
{
    private DependencyGraphCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "dependency-graph",
        Mutates: false,
        JsonOutput: true,
        Category: "analyze",
        Aliases: ["depgraph"],
        WhenToUse: "Visualising the type-level dependency graph for a solution or a specific root type, to understand coupling before a large refactor. Use --format dot to pipe into Graphviz, or --format json for programmatic consumption. Use --type + --depth to focus on a single type's fan-out.",
        Examples:
        [
            "rt analyze dependency-graph --solution C:\\code\\libraries\\ProjectX.Libraries.sln --scope ProjectX.Core",
            "rt analyze depgraph --type BookingService --depth 2 --solution RoslynTools.slnx",
            "rt analyze dependency-graph --format dot --solution RoslynTools.slnx | dot -Tsvg -o deps.svg",
        ],
        KnownIssues:
        [
            "Graph edges are built from fields, method parameters/returns, base types, and interfaces only — local variable type usages inside method bodies are missed. Static command classes with no fields appear as isolated nodes even when they reference WorkspaceLoader/SolutionWriter internally. See ai-improvements row #12.",
        ]
    );

    public static Command Build()
    {
        var typeOpt = new Option<string?>("--type", "Root type to start graph from (optional)");
        var depthOpt = new Option<int?>("--depth", "Maximum traversal depth (optional)");
        var formatOpt = new Option<string?>("--format", "Output format: json (default) or dot");
        var scopeOpt = new Option<string?>("--scope", "Optional namespace prefix to limit analysis");
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");

        var cmd = new Command("dependency-graph", "Build a type-level dependency graph for the solution");
        cmd.AddAlias("depgraph");
        cmd.AddAll(typeOpt, depthOpt, formatOpt, scopeOpt, solutionOpt);

        cmd.SetHandler(async (typeName, depth, format, scope, solution) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var result = await DependencyGraphCore.BuildGraphAsync(sln, typeName, depth, format ?? "json", scope);
                RtConsole.WriteLine(RtOutputContext.Result, result);
            }
        }, typeOpt, depthOpt, formatOpt, scopeOpt, solutionOpt);

        return cmd;
    }

    /// <summary>
    /// Builds a type-level dependency graph and returns it in the requested format.
    /// Delegates to <see cref="DependencyGraphCore.BuildGraphAsync"/>.
    /// </summary>
    internal static Task<string> BuildGraphAsync(
        Solution sln,
        string? typeName,
        int? depth,
        string format,
        string? scope) =>
        DependencyGraphCore.BuildGraphAsync(sln, typeName, depth, format, scope);
}
