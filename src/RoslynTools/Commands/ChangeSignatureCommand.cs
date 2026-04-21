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

        var command = new Command("change-signature", "Change method signatures.")
        {
            renameParam
        };

        return command;
    }
}
