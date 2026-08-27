using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// A command for transforming parameters.
/// </summary>
internal sealed class TransformParametersCommand(
    DaemonRpcRecoveryCoordinator daemon,
    TransformParametersOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly TransformParametersOperation _operation = operation;

    public static RtCommandInfo Info => TransformParametersCommandDefinition.Info;

    public Command Build()
    {
        var methodOpt =
            new Option<string>("--method", "Dot-separated TypeName.MethodName, e.g. OrderService.PlaceOrder")
            { IsRequired = true };
        var newTypeOpt = new Option<string?>("--new-type", "Name of the record to generate, e.g. PlaceOrderArgs");
        var existingTypeOpt = new Option<string?>("--existing-type",
            "Fully-qualified name of an existing type to reuse, e.g. MyApp.PlaceOrderArgs");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln file. Auto-detected if omitted.");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk.");

        var cmd = new Command("transform-parameters",
            "Extract method parameters into a new record and update all call sites.")
        {
            methodOpt, newTypeOpt, existingTypeOpt, solutionOpt, dryRunOpt
        };
        cmd.AddAlias("tp");

        cmd.SetHandler(async context =>
        {
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new TransformParametersRequest(
                    context.ParseResult.GetValueForOption(methodOpt)!,
                    context.ParseResult.GetValueForOption(newTypeOpt),
                    context.ParseResult.GetValueForOption(existingTypeOpt),
                    context.ParseResult.GetValueForOption(solutionOpt),
                    context.ParseResult.GetValueForOption(dryRunOpt)),
                context);
        });

        return cmd;
    }
}
