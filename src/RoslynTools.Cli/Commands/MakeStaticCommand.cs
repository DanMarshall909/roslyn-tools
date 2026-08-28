using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Creates a static command for executing code changes.
/// </summary>
internal sealed class MakeStaticCommand : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon;
    private readonly MakeStaticOperation _operation;

    public MakeStaticCommand(
        DaemonRpcRecoveryCoordinator daemon,
        MakeStaticOperation operation)
    {
        _daemon = daemon;
        _operation = operation;
    }

    public static RtCommandInfo Info => MakeStaticCommandDefinition.Info;

    public Command Build()
    {
        var methodOpt = CommandOptionFactory.CreateMethodOption("--method", "Method to convert: <Type>.<Method>");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("make-static",
            "Convert an instance method to static when it has no instance-state dependencies");
        cmd.AddAlias("mst");
        cmd.AddAll(methodOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async context =>
        {
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new MakeStaticRequest(
                    context.ParseResult.GetValueForOption(methodOpt)!,
                    context.ParseResult.GetValueForOption(solutionOpt),
                    context.ParseResult.GetValueForOption(dryRunOpt)),
                context);
        });

        return cmd;
    }

}
