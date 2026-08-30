using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Converts a method to be asynchronous
/// </summary>
internal sealed class MakeAsyncCommand : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon;
    private readonly MakeAsyncOperation _operation;

    public MakeAsyncCommand(
        DaemonRpcRecoveryCoordinator daemon,
        MakeAsyncOperation operation)
    {
        _daemon = daemon;
        _operation = operation;
    }

    public static RtCommandInfo Info => MakeAsyncCommandDefinition.Info;

    public Command Build()
    {
        var typeOpt = new Option<string>("--type", "Class containing the method") { IsRequired = true };
        var methodOpt = new Option<string>("--method", "Method name to make async") { IsRequired = true };
        var noRenameOpt = new Option<bool>("--no-rename", "Skip renaming to Async suffix");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("make-async", "Convert a synchronous method to async Task<T> and update direct callers");
        cmd.AddAlias("ma");
        cmd.AddAll(typeOpt, methodOpt, noRenameOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async context =>
        {
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new MakeAsyncRequest(
                    context.ParseResult.GetValueForOption(typeOpt)!,
                    context.ParseResult.GetValueForOption(methodOpt)!,
                    context.ParseResult.GetValueForOption(noRenameOpt),
                    context.ParseResult.GetValueForOption(solutionOpt),
                    context.ParseResult.GetValueForOption(dryRunOpt)),
                context);
        });

        return cmd;
    }

}
