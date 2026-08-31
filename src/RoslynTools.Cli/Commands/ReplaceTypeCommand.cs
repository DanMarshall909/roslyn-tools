using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Replaces a type reference with another type.
/// </summary>
internal sealed class ReplaceTypeCommand : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon;
    private readonly ReplaceDateTimeOperation _operation;

    public ReplaceTypeCommand(
        DaemonRpcRecoveryCoordinator daemon,
        ReplaceDateTimeOperation operation)
    {
        _daemon = daemon;
        _operation = operation;
    }

    public static RtCommandInfo Info => ReplaceDateTimeCommandDefinition.Info;

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln file");
        var dryRunOpt = new Option<bool>("--dry-run", () => true, "Preview changes without writing to disk (default)");

        var cmd = new Command("replace-datetime",
            "Replace DateTime.UtcNow / DateTime.Now with injected TimeProvider, adding constructor injection")
        {
            solutionOpt, dryRunOpt
        };

        cmd.SetHandler(async context =>
        {
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new ReplaceDateTimeRequest(
                    context.ParseResult.GetValueForOption(solutionOpt),
                    context.ParseResult.GetValueForOption(dryRunOpt)),
                context);
        });

        return cmd;
    }
}
