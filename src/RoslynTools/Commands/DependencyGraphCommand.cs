using System.CommandLine;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal static class DependencyGraphCommand
{
    internal static Command Build()
    {
        var typeOpt = new Option<string?>("--type", "Root type to start graph from (optional)");
        var depthOpt = new Option<int?>("--depth", "Maximum traversal depth (optional)");
        var formatOpt = new Option<string?>("--format", "Output format: json (default) or dot");
        var scopeOpt = new Option<string?>("--scope", "Optional namespace prefix to limit analysis");
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");

        var cmd = new Command("dependency-graph", "Build a type-level dependency graph for the solution");
        cmd.AddAlias("depgraph");
        cmd.Add(typeOpt);
        cmd.Add(depthOpt);
        cmd.Add(formatOpt);
        cmd.Add(scopeOpt);
        cmd.Add(solutionOpt);

        cmd.SetHandler(async (typeName, depth, format, scope, solution) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var result = await BuildGraphAsync(sln, typeName, depth, format ?? "json", scope);
                Console.WriteLine(result);
            }
        }, typeOpt, depthOpt, formatOpt, scopeOpt, solutionOpt);

        return cmd;
    }

    /// <summary>
    /// Builds a type-level dependency graph and returns it in the requested format.
    /// </summary>
    internal static Task<string> BuildGraphAsync(
        Solution sln,
        string? typeName,
        int? depth,
        string format,
        string? scope)
    {
        throw new NotImplementedException("dependency-graph is not yet implemented.");
    }
}
