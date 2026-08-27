using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Inlines a method into its calling context
/// </summary>
internal sealed class InlineMethodCommand : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon;
    private readonly InlineMethodOperation _operation;

    public InlineMethodCommand(
        DaemonRpcRecoveryCoordinator daemon,
        InlineMethodOperation operation)
    {
        _daemon = daemon;
        _operation = operation;
    }

    public static RtCommandInfo Info => InlineMethodCommandDefinition.Info;

    public Command Build()
    {
        var typeOpt = CommandOptionFactory.CreateTypeOption("--type", "Class containing the method to inline");
        var methodOpt = CommandOptionFactory.CreateMethodOption("--method", "Method name to inline at all call sites");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("inline-method", "Inline a method by replacing all call sites with the method body");
        cmd.AddAlias("im");
        cmd.Add(typeOpt);
        cmd.Add(methodOpt);
        cmd.Add(solutionOpt);
        cmd.Add(dryRunOpt);

        cmd.SetHandler(async context =>
        {
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new InlineMethodRequest(
                    context.ParseResult.GetValueForOption(typeOpt)!,
                    context.ParseResult.GetValueForOption(methodOpt)!,
                    context.ParseResult.GetValueForOption(solutionOpt),
                    context.ParseResult.GetValueForOption(dryRunOpt)),
                context);
        });

        return cmd;
    }

}
