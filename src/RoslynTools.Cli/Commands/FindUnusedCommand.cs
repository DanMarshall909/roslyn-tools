using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Identifies unused commands in the current file
/// </summary>
internal sealed class FindUnusedCommand : IRtCommand, IInspectCommand
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
        var binder = RtCommandFlagBinder.Create()
            .WithSolution("Path to .sln/.slnx");
        var scopeOpt = new Option<string?>("--scope", "Filter: types, methods, or all (default: all)");

        var cmd = new Command("find-unused", "Report types and methods with no references (dead code)");
        cmd.AddAlias("unused");
        cmd.Add(scopeOpt);
        binder.AddTo(cmd);

        cmd.SetHandler(async ctx =>
        {
            var scope = ctx.ParseResult.GetValueForOption(scopeOpt);
            var req = binder.Bind(ctx.ParseResult, f => new FindUnusedCliRequest(f.SolutionPath, scope));

            var sln = await _sessionFactory.GetSolutionAsync(req.SolutionPath);
            var symbols = await _findUnused.AnalyzeAsync(sln, req.Scope ?? "all");
            var result = FindUnusedResult.FromSymbols(symbols, sln.FilePath);
            RtResultRenderer.Write(RtResult<FindUnusedResult>.Ok(result));
        });

        return cmd;
    }

    private sealed record FindUnusedCliRequest(string? SolutionPath, string? Scope) : ISupportsSolutionOption;
}
