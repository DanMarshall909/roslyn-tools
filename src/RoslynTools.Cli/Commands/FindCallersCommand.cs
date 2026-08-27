using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;
using RoslynTools.Services.Results;

namespace RoslynTools.Commands;

/// <summary>
/// Finds all callers of a given symbol in the current file
/// </summary>
internal sealed class FindCallersCommand(
    FindCallersOperation operation,
    DaemonRpcRecoveryCoordinator daemon) : IRtCommand, IInspectCommand
{
    private readonly FindCallersOperation _operation = operation;
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;

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

            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                new FindCallersRequest(request.Symbol!, request.SolutionPath, includeTests),
                ctx.GetCancellationToken());
            result.Match(
                value => RtResultRenderer.Write(RtResult<FindCallersResult>.Ok(ToOutput(value))),
                error => RtErrorPresenter.Surface(ctx, error));
        });

        return cmd;
    }

    private static FindCallersResult ToOutput(FindCallersOperationResult result)
    {
        return new FindCallersResult
        {
            SymbolName = result.SymbolName,
            Callers = result.Callers.Select(static caller => new CallerResult
            {
                FilePath = caller.FilePath,
                Line = caller.Line,
                ContainingMethod = caller.ContainingMethod,
                SourceLine = caller.SourceLine
            }).ToList()
        };
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
