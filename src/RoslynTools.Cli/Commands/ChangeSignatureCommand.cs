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
            "rt change-signature add-param \"MyApp.Services.OrderService.PlaceOrder\" \"int timeout = 30\" --after orderId --solution MyApp.sln",
            "rt change-signature remove-param \"MyApp.Services.OrderService.PlaceOrder\" timeout --solution MyApp.sln",
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

        // ── reorder ────────────────────────────────────────────────────────────
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

        // ── add-param ──────────────────────────────────────────────────────────
        var addMethodSpecArg = new Argument<string>("methodSpec", "Method spec, e.g. MyApp.Services.OrderService.PlaceOrder");
        var paramSpecArg = new Argument<string>("paramSpec", "Parameter declaration, e.g. \"int timeout = 30\"");
        var afterOpt = new Option<string?>("--after", "Insert the new parameter immediately after this existing parameter name.");
        var addSolutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file. Defaults to the single solution in current directory.");
        var addDryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk.");

        var addParam = new Command("add-param", "Add a parameter to a method signature (must have a default value).")
        {
            addMethodSpecArg,
            paramSpecArg,
            afterOpt,
            addSolutionOpt,
            addDryRunOpt
        };

        addParam.SetHandler(async context =>
        {
            var methodSpec = context.ParseResult.GetValueForArgument(addMethodSpecArg);
            var paramSpec = context.ParseResult.GetValueForArgument(paramSpecArg);
            var after = context.ParseResult.GetValueForOption(afterOpt);
            var solution = context.ParseResult.GetValueForOption(addSolutionOpt);
            var dryRun = context.ParseResult.GetValueForOption(addDryRunOpt);

            var (workspace, loadedSolution) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                try
                {
                    var updatedSolution = await ChangeSignatureCore.AddParamAsync(
                        loadedSolution,
                        methodSpec,
                        paramSpec,
                        after);

                    await SolutionWriter.ApplyChangesAsync(loadedSolution, updatedSolution, dryRun);
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    context.ExitCode = 1;
                }
            }
        });

        // ── remove-param ───────────────────────────────────────────────────────
        var removeMethodSpecArg = new Argument<string>("methodSpec", "Method spec, e.g. MyApp.Services.OrderService.PlaceOrder");
        var paramNameArg = new Argument<string>("paramName", "Name of the parameter to remove.");
        var forceOpt = new Option<bool>("--force", "Remove even if the parameter has no default value.");
        var removeSolutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file. Defaults to the single solution in current directory.");
        var removeDryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk.");

        var removeParam = new Command("remove-param", "Remove a parameter from a method signature and update all call sites.")
        {
            removeMethodSpecArg,
            paramNameArg,
            forceOpt,
            removeSolutionOpt,
            removeDryRunOpt
        };

        removeParam.SetHandler(async context =>
        {
            var methodSpec = context.ParseResult.GetValueForArgument(removeMethodSpecArg);
            var paramName = context.ParseResult.GetValueForArgument(paramNameArg);
            var force = context.ParseResult.GetValueForOption(forceOpt);
            var solution = context.ParseResult.GetValueForOption(removeSolutionOpt);
            var dryRun = context.ParseResult.GetValueForOption(removeDryRunOpt);

            var (workspace, loadedSolution) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                try
                {
                    var updatedSolution = await ChangeSignatureCore.RemoveParamAsync(
                        loadedSolution,
                        methodSpec,
                        paramName,
                        force);

                    await SolutionWriter.ApplyChangesAsync(loadedSolution, updatedSolution, dryRun);
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    context.ExitCode = 1;
                }
            }
        });

        var command = new Command("change-signature", "Change method signatures.")
        {
            renameParam,
            reorder,
            addParam,
            removeParam
        };

        return command;
    }
}
