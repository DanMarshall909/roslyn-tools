using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Extracts string literals from the current file
/// </summary>
internal sealed class ExtractStringConstantCommand : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon;
    private readonly ExtractStringConstantOperation _operation;

    public ExtractStringConstantCommand(
        DaemonRpcRecoveryCoordinator daemon,
        ExtractStringConstantOperation operation)
    {
        _daemon = daemon;
        _operation = operation;
    }

    public static RtCommandInfo Info => ExtractStringConstantCommandDefinition.Info;

    public Command Build()
    {
        var fileOpt = CommandOptionFactory.CreateStringOption("--file", "Path to the .cs file containing the literal");
        var lineOpt = new Option<int>("--line", "Line number of the string literal to extract") { IsRequired = true };
        var nameOpt = CommandOptionFactory.CreateStringOption("--name", "Constant name to create");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("extract-string-constant", "Extract a string literal into a private const string");
        cmd.AddAlias("esc");
        cmd.AddAll(fileOpt, lineOpt, nameOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async context =>
        {
            var request = new ExtractStringConstantRequest(
                context.ParseResult.GetValueForOption(fileOpt)!,
                context.ParseResult.GetValueForOption(lineOpt),
                context.ParseResult.GetValueForOption(nameOpt)!,
                context.ParseResult.GetValueForOption(solutionOpt),
                context.ParseResult.GetValueForOption(dryRunOpt));
            await DaemonCommandExecutor.ExecuteAsync(_daemon, _operation, request, context);
        });

        return cmd;
    }
}
