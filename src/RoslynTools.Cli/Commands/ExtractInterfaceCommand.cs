using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Extracts an interface from the solution.
/// </summary>
internal sealed class ExtractInterfaceCommand(
    DaemonRpcRecoveryCoordinator daemon,
    ExtractInterfaceOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly ExtractInterfaceOperation _operation = operation;
    public static RtCommandInfo Info => ExtractInterfaceCommandDefinition.Info;

    public Command Build()
    {
        var typeOpt = new Option<string>("--type", "Class to extract interface from") { IsRequired = true };
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var outputDirOpt = new Option<string?>("--output-dir", "Directory for the new interface file");
        var namespaceOpt = new Option<string?>("--namespace", "Namespace override for the interface");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("extract-interface", "Generate IXxx interface from a class's public members");
        cmd.AddAlias("ei");
        cmd.AddAll(typeOpt, solutionOpt, outputDirOpt, namespaceOpt, dryRunOpt);

        cmd.SetHandler(async context =>
        {
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new ExtractInterfaceRequest(
                    context.ParseResult.GetValueForOption(typeOpt)!,
                    context.ParseResult.GetValueForOption(outputDirOpt),
                    context.ParseResult.GetValueForOption(namespaceOpt),
                    context.ParseResult.GetValueForOption(solutionOpt),
                    context.ParseResult.GetValueForOption(dryRunOpt)),
                context);
        });

        return cmd;
    }
}
