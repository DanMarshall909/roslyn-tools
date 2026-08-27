using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for adding null checks to the project.
/// </summary>
internal sealed class AddNullChecksCommand(
    DaemonRpcRecoveryCoordinator daemon,
    AddNullChecksOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly AddNullChecksOperation _operation = operation;
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;

    public static RtCommandInfo Info => AddNullChecksCommandDefinition.Info;

    public Command Build()
    {
        var typeOpt = CommandOptionFactory.CreateTypeOption("--type", "Class containing the method");
        var methodOpt = CommandOptionFactory.CreateMethodOption("--method", "Method name to add null checks to");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("add-null-checks",
            "Insert ArgumentNullException.ThrowIfNull guards for reference-type parameters");
        cmd.AddAlias("anc");
        cmd.AddAll(typeOpt, methodOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async context =>
        {
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new AddNullChecksRequest(
                    context.ParseResult.GetValueForOption(typeOpt)!,
                    context.ParseResult.GetValueForOption(methodOpt)!,
                    context.ParseResult.GetValueForOption(solutionOpt),
                    context.ParseResult.GetValueForOption(dryRunOpt)),
                context);
        });

        return cmd;
    }
}
