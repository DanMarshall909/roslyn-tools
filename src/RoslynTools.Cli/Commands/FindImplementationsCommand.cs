using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Finds all implementations of a given symbol in the current file
/// </summary>
internal sealed class FindImplementationsCommand(DaemonRpcClient daemon) : IRtCommand, IStructuredOutputCommand, IInspectCommand
{
    private readonly DaemonRpcClient _daemon = daemon;

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
                await DaemonCommandExecutor.ExecuteAsync(
                    _daemon,
                    FindImplementationsCommandDefinition.Name,
                    new FindImplementationsRequest(symbol, solution),
                    ctx);
            });

        return cmd;
    }
}
