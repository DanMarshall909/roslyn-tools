using System.CommandLine;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class ChangeSignatureCommand : IRtCommand
{
    private ChangeSignatureCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "change-signature",
        Mutates: true,
        JsonOutput: false,
        Category: "Mutation",
        WhenToUse: "Change method signatures and update affected call sites.",
        Examples:
        [
            "rt change-signature rename-param \"MyApp.Services.OrderService.PlaceOrder\" oldName newName --solution MyApp.sln",
            "rt change-signature reorder \"MyApp.Services.OrderService.PlaceOrder\" \"2,0,1\" --solution MyApp.sln",
        ]
    );

    public static Command Build()
    {
        var methodSpecArg = new Argument<string>("methodSpec", "Method spec, e.g. MyApp.Services.OrderService.PlaceOrder");
        var oldNameArg = new Argument<string>("oldName", "Existing parameter name.");
        var newNameArg = new Argument<string>("newName", "Replacement parameter name.");
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file. Defaults to the single solution in current directory.");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk.");

        var renameParam = new Command("rename-param", "Rename a method parameter and update named-argument call sites.")
        {
            methodSpecArg,
            oldNameArg,
            newNameArg,
            solutionOpt,
            dryRunOpt
        };

        renameParam.SetHandler(async context =>
        {
            var methodSpec = context.ParseResult.GetValueForArgument(methodSpecArg);
            var oldName = context.ParseResult.GetValueForArgument(oldNameArg);
            var newName = context.ParseResult.GetValueForArgument(newNameArg);
            var solution = context.ParseResult.GetValueForOption(solutionOpt);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOpt);

            var (workspace, loadedSolution) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                try
                {
                    var updatedSolution = await ChangeSignatureCore.RenameParamAsync(
                        loadedSolution,
                        methodSpec,
                        oldName,
                        newName);

                    await SolutionWriter.ApplyChangesAsync(loadedSolution, updatedSolution, dryRun);
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    context.ExitCode = 1;
                    return;
                }
            }
        });

        var reorderMethodSpecArg = new Argument<string>("methodSpec", "Method spec, e.g. MyApp.Services.OrderService.PlaceOrder");
        var orderArg = new Argument<string>("order", "Comma-separated new parameter order: indices (\"2,0,1\") or names (\"c,a,b\").");
        var reorderSolutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file. Defaults to the single solution in current directory.");
        var reorderDryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk.");

        var reorder = new Command("reorder", "Reorder method parameters and update all call sites.")
        {
            reorderMethodSpecArg,
            orderArg,
            reorderSolutionOpt,
            reorderDryRunOpt
        };

        reorder.SetHandler(async context =>
        {
            var methodSpec = context.ParseResult.GetValueForArgument(reorderMethodSpecArg);
            var orderRaw = context.ParseResult.GetValueForArgument(orderArg);
            var solution = context.ParseResult.GetValueForOption(reorderSolutionOpt);
            var dryRun = context.ParseResult.GetValueForOption(reorderDryRunOpt);

            var newOrder = orderRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var (workspace, loadedSolution) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                try
                {
                    var updatedSolution = await ChangeSignatureCore.ReorderParamsAsync(
                        loadedSolution,
                        methodSpec,
                        newOrder);

                    await SolutionWriter.ApplyChangesAsync(loadedSolution, updatedSolution, dryRun);
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    context.ExitCode = 1;
                    return;
                }
            }
        });

        var command = new Command("change-signature", "Change method signatures.")
        {
            renameParam,
            reorder
        };

        return command;
    }
}
