using System.CommandLine;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal static class FindUnusedCommand
{
    internal static Command Build()
    {
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var scopeOpt = new Option<string?>("--scope", "Filter: types, methods, or all (default: all)");

        var cmd = new Command("find-unused", "Report types and methods with no references (dead code)");
        cmd.AddAlias("unused");
        cmd.Add(solutionOpt);
        cmd.Add(scopeOpt);

        cmd.SetHandler(async (solution, scope) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var results = await FindUnusedCore.AnalyzeAsync(sln, scope ?? "all");
                foreach (var r in results)
                    Console.WriteLine($"{r.Kind,-10} {r.Name,-40} {r.FilePath}:{r.Line}");
            }
        }, solutionOpt, scopeOpt);

        return cmd;
    }
}
