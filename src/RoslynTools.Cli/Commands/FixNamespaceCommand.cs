using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Fixes namespace issues in the current file
/// </summary>
internal sealed class FixNamespaceCommand : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon;
    private readonly FixNamespaceOperation _operation;

    public FixNamespaceCommand(
        DaemonRpcRecoveryCoordinator daemon,
        FixNamespaceOperation operation)
    {
        _daemon = daemon;
        _operation = operation;
    }

    public static RtCommandInfo Info => FixNamespaceCommandDefinition.Info;

    public Command Build()
    {
        var fileOpt = CommandOptionFactory.CreateStringOption("--file", "Path to the .cs file to fix");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("fix-namespace", "Align a file's namespace declaration to its folder path");
        cmd.AddAlias("fns");
        cmd.AddAll(fileOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async context =>
        {
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new FixNamespaceRequest(
                    context.ParseResult.GetValueForOption(fileOpt)!,
                    context.ParseResult.GetValueForOption(solutionOpt),
                    context.ParseResult.GetValueForOption(dryRunOpt)),
                context);
        });

        return cmd;
    }

}
