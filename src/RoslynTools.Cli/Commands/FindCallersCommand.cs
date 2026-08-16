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
    private readonly FindCallersOperation _operation;
    public FindCallersCommand(FindCallersOperation operation)
    {
        _operation = operation;
    }

    public static RtCommandInfo Info => FindCallersCommandDefinition.Info;

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

            var result = await _operation.ExecuteAsync(new FindCallersRequest(
                request.Symbol!, request.SolutionPath, includeTests));
            result.Match(
                callers => RtResultRenderer.Write(RtResult<FindCallersResult>.Ok(callers)),
                error => RtErrorPresenter.Surface(ctx, error));
        });

        return cmd;
    }

    internal bool IsNameofExpression(InvocationExpressionSyntax invocation)
    {
        return _operation.IsNameofExpression(invocation);
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
