using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class MakeSealedCommand : IRtCommand
{
    private MakeSealedCommand() { }

    // Stryker disable all
    public static RtCommandInfo Info { get; } = new(
        Name: "make-sealed",
        Mutates: true,
        JsonOutput: false,
        Category: "refactor",
        Aliases: ["ms"],
        WhenToUse: "Adding the sealed modifier to a class that has no derived types in the solution. Sealing classes improves runtime dispatch performance and communicates design intent. The command verifies no derived classes exist before modifying, making it safe to run without a prior manual check.",
        Examples:
        [
            "rt refactor make-sealed --type BookingValidator --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor ms --type CsvFormatter --dry-run",
            "rt refactor make-sealed --type RtCommandInfo --solution RoslynTools.slnx",
        ]
    );
    // Stryker restore all

    // Stryker disable all
    public static Command Build()
    {
        var typeOpt = CommandOptionFactory.CreateTypeOption("--type", "Class to seal");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("make-sealed", "Add the sealed modifier to a class that has no derived types");
        cmd.AddAlias("ms");
        cmd.AddAll(typeOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (type, solution, dryRun) =>
        {
            await ExecuteAsync(type, solution, dryRun);
        }, typeOpt, solutionOpt, dryRunOpt);

        return cmd;
    }
    // Stryker restore all

    internal static async Task ExecuteAsync(string typeName, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            await RtServiceProvider.Get<MakeSealedService>().RunAsync(sln, typeName, dryRun);
        }
    }
}

