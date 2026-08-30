using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Migrates code to use IOptions interface for configuration.
/// </summary>
internal sealed class MigrateToIOptionsCommand(
    DaemonRpcRecoveryCoordinator daemon,
    MigrateToIOptionsOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly MigrateToIOptionsOperation _operation = operation;
    public static RtCommandInfo Info => MigrateToIOptionsCommandDefinition.Info;

    public Command Build()
    {
        var sectionOpt = new Option<string?>("--section",
            "Configuration section name, for example MyFeature or Parent:Child");
        var nameOpt = new Option<string?>("--name", "Options POCO type name in PascalCase");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("migrate-to-ioptions",
            "Create an options POCO and register it with services.Configure<T>");
        cmd.AddAlias("mti");
        cmd.AddAll(sectionOpt, nameOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async context =>
        {
            var request = new MigrateToIOptionsRequest(
                context.ParseResult.GetValueForOption(sectionOpt)!,
                context.ParseResult.GetValueForOption(nameOpt)!,
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
