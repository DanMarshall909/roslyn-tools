using System.CommandLine;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class FindUnusedCommand : IRtCommand
{
    private FindUnusedCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "find-unused",
        Mutates: false,
        JsonOutput: false,
        Category: "Navigation",
        Aliases: ["unused"],
        WhenToUse: "Identifying dead code — types and methods with zero references in the solution — before a safe-delete pass or ahead of a major refactor. Use --scope to focus on a specific namespace and filter noise from generated or test code.",
        Examples:
        [
            "rt find-unused --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt unused --scope ProjectX.Core --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt find-unused --scope types --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
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
