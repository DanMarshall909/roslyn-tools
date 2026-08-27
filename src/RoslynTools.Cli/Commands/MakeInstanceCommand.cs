using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Creates an instance of a given type
/// </summary>
internal sealed class MakeInstanceCommand : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon;
    private readonly MakeInstanceOperation _operation;

    public MakeInstanceCommand(
        DaemonRpcRecoveryCoordinator daemon,
        MakeInstanceOperation operation)
    {
        _daemon = daemon;
        _operation = operation;
    }

    public static RtCommandInfo Info => MakeInstanceCommandDefinition.Info;

    public Command Build()
    {
        var methodOpt =
            CommandOptionFactory.CreateMethodOption("--method", "Fully-qualified method to convert: Type.Method");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("make-instance", "Remove static from a method and rewrite call sites to use an instance");
        cmd.AddAlias("mns");
        cmd.AddAll(methodOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async context =>
        {
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new MakeInstanceRequest(
                    context.ParseResult.GetValueForOption(methodOpt)!,
                    context.ParseResult.GetValueForOption(solutionOpt),
                    context.ParseResult.GetValueForOption(dryRunOpt)),
                context);
        });

        return cmd;
    }

}
