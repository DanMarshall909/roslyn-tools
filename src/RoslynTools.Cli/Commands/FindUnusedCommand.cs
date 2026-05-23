using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Identifies unused commands in the current file
/// </summary>
internal sealed class FindUnusedCommand : IRtCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly FindUnusedService _findUnused;
    public FindUnusedCommand(IWorkspaceSessionFactory sessionFactory, FindUnusedService findUnused)
    {
        _findUnused = findUnused;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "find-unused",
        false,
        false,
        "inspect",
        Aliases: ["unused"],
        WhenToUse:
        "Identifying dead code — types and methods with zero references in the solution — before a safe-delete pass or ahead of a major refactor. Use --scope to focus on a specific namespace and filter noise from generated or test code.",
        Examples:
        [
            "rt inspect find-unused --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect unused --scope ProjectX.Core --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-unused --scope types --solution RoslynTools.slnx"
        ],
        SeeAlso: ["safe-delete", "find-refs"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]
    );

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var scopeOpt = new Option<string?>("--scope", "Filter: types, methods, or all (default: all)");

        var cmd = new Command("find-unused", "Report types and methods with no references (dead code)");
        cmd.AddAlias("unused");
        cmd.Add(solutionOpt);
        cmd.Add(scopeOpt);

        cmd.SetHandler(async ctx =>
        {
            var solution = ctx.ParseResult.GetValueForOption(solutionOpt);
            var scope = ctx.ParseResult.GetValueForOption(scopeOpt);

            var sln = await _sessionFactory.GetSolutionAsync(solution);
            var symbols = await _findUnused.AnalyzeAsync(sln, scope ?? "all");
            var result = FindUnusedResult.FromSymbols(symbols);
            RtResultRenderer.WriteOutput(RtResult<FindUnusedResult>.Ok(result));
        });

        return cmd;
    }
}
