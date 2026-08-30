using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Finds all implementations of a given symbol in the current file
/// </summary>
internal sealed class FindImplementationsCommand(
    DaemonRpcRecoveryCoordinator daemon,
    FindImplementationsOperation operation) : IRtCommand, IStructuredOutputCommand, IInspectCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly FindImplementationsOperation _operation = operation;

    public static RtCommandInfo Info => FindImplementationsCommandDefinition.Info;

    public Command Build()
    {
        var symbolArg = new Argument<string>("symbol", "Fully-qualified interface or abstract class name");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln/.slnx file");

        var cmd = new Command("find-impl", "Find all implementations of an interface or abstract class (JSON)")
        {
            symbolArg, solutionOpt
        };
        cmd.AddAlias("impls");

        cmd.SetHandler(async ctx =>
        {
            var symbol = ctx.ParseResult.GetValueForArgument(symbolArg);
            var solution = ctx.ParseResult.GetValueForOption(solutionOpt);
            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                new FindImplementationsRequest(symbol, solution),
                ctx.GetCancellationToken());
            result.Match(
                value => RtResultRenderer.Write(RtResult<FindImplementationsResult>.Ok(value)),
                error => RtErrorPresenter.Surface(ctx, error));
        });

        return cmd;
    }
}
