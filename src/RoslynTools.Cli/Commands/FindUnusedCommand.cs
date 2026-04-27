using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class FindUnusedCommand : IRtCommand
{
    private FindUnusedCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "find-unused",
        Mutates: false,
        JsonOutput: false,
        Category: "inspect",
        Aliases: ["unused"],
        WhenToUse: "Identifying dead code — types and methods with zero references in the solution — before a safe-delete pass or ahead of a major refactor. Use --scope to focus on a specific namespace and filter noise from generated or test code.",
        Examples:
        [
            "rt inspect find-unused --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect unused --scope ProjectX.Core --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-unused --scope types --solution RoslynTools.slnx",
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]
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
                var symbols = await FindUnusedService.AnalyzeAsync(sln, scope ?? "all");
                var result = FindUnusedResult.FromSymbols(symbols);
                RtSerializer.Write(result);
            }
        }, solutionOpt, scopeOpt);

        return cmd;
    }
}
