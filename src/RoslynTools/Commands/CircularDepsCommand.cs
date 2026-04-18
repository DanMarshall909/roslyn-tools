using System.CommandLine;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal static class CircularDepsCommand
{
    internal static Command Build()
    {
        var scopeOpt = new Option<string?>("--scope", "Optional namespace prefix to limit analysis");
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");

        var cmd = new Command("circular-deps", "Detect circular namespace/type dependencies in the solution");
        cmd.AddAlias("cdeps");
        cmd.Add(scopeOpt);
        cmd.Add(solutionOpt);

        cmd.SetHandler(async (scope, solution) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var result = await AnalyzeAsync(sln, scope);
                Console.WriteLine(result);
            }
        }, scopeOpt, solutionOpt);

        return cmd;
    }

    /// <summary>
    /// Analyzes the solution for circular namespace dependencies.
    /// Returns a human-readable string describing any cycles found.
    /// </summary>
    internal static Task<string> AnalyzeAsync(Solution sln, string? scope)
    {
        throw new NotImplementedException("circular-deps is not yet implemented.");
    }
}
