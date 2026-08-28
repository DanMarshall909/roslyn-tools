using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Extracts a class from the solution.
/// </summary>
internal sealed class ExtractClassCommand(
    DaemonRpcRecoveryCoordinator daemon,
    ExtractClassOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly ExtractClassOperation _operation = operation;
    public static RtCommandInfo Info => ExtractClassCommandDefinition.Info;

    public Command Build()
    {
        var typeOpt = new Option<string>("--type", "Source class name") { IsRequired = true };
        var membersOpt = new Option<string>("--members", "Comma-separated member names to extract")
        { IsRequired = true };
        var newTypeOpt = new Option<string>("--new-type", "Name for the new class") { IsRequired = true };
        var staticOpt = new Option<bool>("--static", "Make the new class static (no injection)");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var outputDirOpt = new Option<string?>("--output-dir", "Directory for new file");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("extract-class", "Move named members from a class into a new class file");
        cmd.AddAlias("ec");
        cmd.AddAll(typeOpt, membersOpt, newTypeOpt, staticOpt, solutionOpt, outputDirOpt, dryRunOpt);

        cmd.SetHandler(async context =>
        {
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new ExtractClassRequest(
                    context.ParseResult.GetValueForOption(typeOpt)!,
                    context.ParseResult.GetValueForOption(membersOpt)!
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    context.ParseResult.GetValueForOption(newTypeOpt)!,
                    context.ParseResult.GetValueForOption(staticOpt),
                    context.ParseResult.GetValueForOption(outputDirOpt),
                    context.ParseResult.GetValueForOption(solutionOpt),
                    context.ParseResult.GetValueForOption(dryRunOpt)),
                context);
        });

        return cmd;
    }
}
