using System.CommandLine;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Finds all callers of a given symbol in the current file
/// </summary>
internal sealed class FindCallersCommand(
    FindCallersOperation operation,
    DaemonRpcClient daemon) : IRtCommand, IInspectCommand
{
    private readonly FindCallersOperation _operation = operation;
    private readonly DaemonRpcClient _daemon = daemon;

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

            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                FindCallersCommandDefinition.Name,
                new FindCallersRequest(request.Symbol!, request.SolutionPath, includeTests),
                ctx);
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
