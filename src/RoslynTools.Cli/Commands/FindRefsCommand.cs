using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;
using RoslynTools.Services.Results;

namespace RoslynTools.Commands;

/// <summary>
/// Finds all references to a given symbol in the current file
/// </summary>
internal sealed class FindRefsCommand : IRtCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly FindRefsService _findRefs;
    public FindRefsCommand(IWorkspaceSessionFactory sessionFactory, FindRefsService findRefs)
    {
        _findRefs = findRefs;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "find-refs",
        false,
        false,
        "inspect",
        Aliases: null,
        WhenToUse:
        "Finding all references to any symbol (type, method, property, field, namespace) across the solution using Roslyn's semantic analysis. Prefer this over grep because it resolves overloads and type inference correctly and skips string literals/comments.",
        Examples:
        [
            "rt inspect find-refs --symbol BookingService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-refs --symbol CreateBooking --kind method --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-refs --symbol IEmailService --kind type --solution RoslynTools.slnx"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]
    );

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln/.slnx file");
        var symbolOpt = new Option<string>("--symbol", "Simple name of the symbol") { IsRequired = true };
        var kindOpt = new Option<string?>("--kind", "Symbol kind: type, method, property, field, namespace");

        var cmd = new Command("find-refs", "Find all references to a symbol across the solution")
        {
            solutionOpt, symbolOpt, kindOpt
        };

        cmd.SetHandler(async ctx =>
        {
            var solution = ctx.ParseResult.GetValueForOption(solutionOpt);
            var symbol = ctx.ParseResult.GetValueForOption(symbolOpt)!;
            var kind = ctx.ParseResult.GetValueForOption(kindOpt);
            var sln = await _sessionFactory.GetSolutionAsync(solution);
            var result = await _findRefs.FindAsync(sln, symbol, kind);
            RtResultRenderer.WriteOutput(RtResult<FindRefsResult>.Ok(result));
        });

        return cmd;
    }
}
