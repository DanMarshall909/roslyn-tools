using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for adding global imports to the project.
/// </summary>
internal sealed class AddGlobalImportCommand(
    DaemonRpcRecoveryCoordinator daemon,
    AddGlobalImportOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly AddGlobalImportOperation _operation = operation;

    public static RtCommandInfo Info => AddGlobalImportCommandDefinition.Info;

    public Command Build()
    {
        var fileOpt = CommandOptionFactory.CreateFileOption();
        var namespaceOpt = CommandOptionFactory.CreateNamespaceOption();
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("add-global-import", "Add a global using directive to an existing file");
        cmd.AddAlias("agi");
        cmd.AddAll(fileOpt, namespaceOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async context =>
        {
            var request = new AddGlobalImportRequest(
                context.ParseResult.GetValueForOption(fileOpt)!,
                context.ParseResult.GetValueForOption(namespaceOpt)!,
                context.ParseResult.GetValueForOption(solutionOpt),
                context.ParseResult.GetValueForOption(dryRunOpt));
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                request,
                context);
        });

        return cmd;
    }
}
