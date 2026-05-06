using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class TransformParametersCommand : IRtCommand
{
    private TransformParametersCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "transform-parameters",
        Mutates: true,
        JsonOutput: false,
        Category: "refactor",
        Aliases: ["tp"],
        WhenToUse: "Introduce a Parameter Object: extract a method's parameters into a new record and update all call sites. Useful when a method has 3+ parameters that logically belong together.",
        Examples:
        [
            "rt refactor transform-parameters --method OrderService.PlaceOrder --new-type PlaceOrderArgs --solution MySolution.sln",
            "rt refactor tp --method BookingService.Create --new-type CreateBookingArgs --dry-run",
            "rt refactor transform-parameters --method Calculator.Add --existing-type MyApp.AddArgs --solution RoslynTools.slnx --dry-run",
        ]
    );

    public static Command Build()
    {
        var methodOpt = new Option<string>("--method", "Dot-separated TypeName.MethodName, e.g. OrderService.PlaceOrder") { IsRequired = true };
        var newTypeOpt = new Option<string?>("--new-type", "Name of the record to generate, e.g. PlaceOrderArgs");
        var existingTypeOpt = new Option<string?>("--existing-type", "Fully-qualified name of an existing type to reuse, e.g. MyApp.PlaceOrderArgs");
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln file. Auto-detected if omitted.");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk.");

        var cmd = new Command("transform-parameters", "Extract method parameters into a new record and update all call sites.")
        {
            methodOpt, newTypeOpt, existingTypeOpt, solutionOpt, dryRunOpt
        };
        cmd.AddAlias("tp");

        cmd.SetHandler(async (method, newType, existingType, solution, dryRun) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                try
                {
                    await new TransformParametersService().RunAsync(sln, method, newType, existingType, dryRun);
                }
                catch (InvalidOperationException ex)
                {
                    RtLog.Error("Error: {Message}", ex.Message);
                    Environment.ExitCode = 1;
                }
            }
        }, methodOpt, newTypeOpt, existingTypeOpt, solutionOpt, dryRunOpt);

        return cmd;
    }
}
