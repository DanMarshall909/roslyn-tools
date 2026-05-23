using System.CommandLine;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;
using RoslynTools.Services.Results;

namespace RoslynTools.Commands;

/// <summary>
/// Finds all callers of a given symbol in the current file
/// </summary>
internal sealed class FindCallersCommand : IRtCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly FindCallersService _findCallers;
    public FindCallersCommand(IWorkspaceSessionFactory sessionFactory, FindCallersService findCallers)
    {
        _findCallers = findCallers;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "find-callers",
        false,
        false,
        "inspect",
        Aliases: ["callers"],
        WhenToUse:
        "Finding all invocation sites of a method before renaming or deleting it, or to understand the blast radius of a signature change. Unlike find-refs, this filters to true call sites only, excluding nameof() and other non-invocation references.",
        Examples:
        [
            "rt inspect find-callers --symbol SendEmail --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect callers --symbol ProcessPayment --include-tests --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-callers --symbol BuildGraphAsync --solution RoslynTools.slnx"
        ],
        SeeAlso: ["find-refs", "find-impl", "change-signature"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]
    );

    public Command Build()
    {
        var symbolOpt = new Option<string>("--symbol", "Method name to find callers of") { IsRequired = true };
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var includeTestsOpt = CommandOptionFactory.CreateIncludeTestsOption();

        var cmd = new Command("find-callers", "Find all invocation sites for a method");
        cmd.AddAlias("callers");
        cmd.Add(symbolOpt);
        cmd.Add(solutionOpt);
        cmd.Add(includeTestsOpt);

        cmd.SetHandler(async ctx =>
        {
            var symbol = ctx.ParseResult.GetValueForOption(symbolOpt)!;
            var solution = ctx.ParseResult.GetValueForOption(solutionOpt);
            var includeTests = ctx.ParseResult.GetValueForOption(includeTestsOpt);

            var sln = await _sessionFactory.GetSolutionAsync(solution);
            var result = await _findCallers.FindAsync(sln, symbol, includeTests);
            RtResultRenderer.WriteOutput(RtResult<FindCallersResult>.Ok(result));
        });

        return cmd;
    }

    internal bool IsNameofExpression(InvocationExpressionSyntax invocation)
    {
        return _findCallers.IsNameofExpression(invocation);
    }
}
