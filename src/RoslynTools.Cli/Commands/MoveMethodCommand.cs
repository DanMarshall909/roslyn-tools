using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Moves a method from one location to another in the solution.
/// </summary>
internal sealed class MoveMethodCommand(
    DaemonRpcRecoveryCoordinator daemon,
    MoveMethodOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly MoveMethodOperation _operation = operation;
    public static RtCommandInfo Info => MoveMethodCommandDefinition.Info;

    public Command Build()
    {
        var methodFqnArg = new Argument<string>("method-fqn", "Fully-qualified method name (e.g. App.Source.Add)");
        var toTypeOpt = new Option<string>("--to-type", "Fully-qualified target class name (e.g. App.Target)")
        { IsRequired = true };
        var createFileOpt = new Option<bool>("--create-file", "Create the target class file if it does not exist");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx. Auto-detected if omitted.");
        var dryRunOpt = new Option<bool>("--dry-run", () => true, "Preview changes without writing to disk (default)");

        var cmd = new Command("move-method", "Move a static method to another class, rewriting all call sites")
        {
            methodFqnArg, toTypeOpt, createFileOpt, solutionOpt, dryRunOpt
        };

        cmd.SetHandler(async context =>
        {
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new MoveMethodRequest(
                    context.ParseResult.GetValueForArgument(methodFqnArg),
                    context.ParseResult.GetValueForOption(toTypeOpt)!,
                    context.ParseResult.GetValueForOption(createFileOpt),
                    context.ParseResult.GetValueForOption(solutionOpt),
                    context.ParseResult.GetValueForOption(dryRunOpt)),
                context);
        });

        return cmd;
    }
}
