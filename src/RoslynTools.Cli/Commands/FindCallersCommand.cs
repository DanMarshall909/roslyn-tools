using System.CommandLine;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;
using RoslynTools.Services.Results;

namespace RoslynTools.Commands;

/// <summary>
/// Finds all callers of a given symbol in the current file
/// </summary>
internal sealed class FindCallersCommand : IRtCommand, IInspectCommand
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
        var flags = RtCommandFlagBinder.Create()
            .WithSolution()
            .WithSymbol("Method name to find callers of");
        var includeTestsOpt = CommandOptionFactory.CreateIncludeTestsOption();

        var cmd = new Command("find-callers", "Find all invocation sites for a method");
        cmd.AddAlias("callers");
        flags.AddTo(cmd);
        cmd.Add(includeTestsOpt);

        cmd.SetHandler(async ctx =>
        {
            var request = flags.Bind(ctx.ParseResult, FindCallersCliRequest.FromFlags);
            var includeTests = ctx.ParseResult.GetValueForOption(includeTestsOpt);

            var sln = await _sessionFactory.GetSolutionAsync(request.SolutionPath);
            var result = await _findCallers.FindAsync(sln, request.Symbol!, includeTests);
            result.Match(
                callers => RtResultRenderer.Write(RtResult<FindCallersResult>.Ok(callers)),
                error => RtErrorPresenter.Surface(ctx, error));
        });

        return cmd;
    }

    internal bool IsNameofExpression(InvocationExpressionSyntax invocation)
    {
        return _findCallers.IsNameofExpression(invocation);
    }

    private sealed record FindCallersCliRequest(
        string? SolutionPath,
        string? Symbol) : ISupportsSolutionOption, ISupportsSymbolOption
    {
        public static FindCallersCliRequest FromFlags(RtFlagValues flags)
        {
            return new FindCallersCliRequest(flags.SolutionPath, flags.Symbol);
        }
    }
}
