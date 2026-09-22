using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>Removes compiler-confirmed unnecessary C# using directives.</summary>
internal sealed class RemoveUnusedUsingsCommand(
    DaemonRpcRecoveryCoordinator daemon,
    RemoveUnusedUsingsOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly RemoveUnusedUsingsOperation _operation = operation;

    public static RtCommandInfo Info => RemoveUnusedUsingsCommandDefinition.Info;

    public Command Build()
    {
        var fileArg = new Argument<string?>(
            "file",
            () => null,
            "Optional .cs file path; omit to process every eligible file in the solution");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();
        var applyOpt = new Option<bool>("--apply", "Write the reviewed removals to disk");
        var command = new Command(
            "remove-unused-usings",
            "Remove compiler-confirmed unnecessary C# using directives")
        {
            fileArg,
            solutionOpt,
            dryRunOpt,
            applyOpt
        };
        command.AddAlias("remove-unused-imports");

        command.SetHandler(async context =>
        {
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new RemoveUnusedUsingsRequest(
                    context.ParseResult.GetValueForArgument(fileArg),
                    context.ParseResult.GetValueForOption(solutionOpt),
                    DryRun: context.ParseResult.GetValueForOption(dryRunOpt)
                            && !context.ParseResult.GetValueForOption(applyOpt)),
                context);
        });

        return command;
    }
}
