using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class AddNullChecksCommand : IRtCommand
{
    private AddNullChecksCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "add-null-checks",
        Mutates: true,
        JsonOutput: false,
        Category: "refactor",
        Aliases: ["anc"],
        WhenToUse: "Retrofitting ArgumentNullException.ThrowIfNull guards onto reference-type parameters in an existing method. Prefer this over a manual edit when a method has several parameters and you want to skip any that are already guarded or are nullable (annotated with ?).",
        Examples:
        [
            "rt refactor add-null-checks --type BookingService --method CreateBooking --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor anc --type OrderProcessor --method Process --dry-run",
            "rt refactor add-null-checks --type ReportController --method Generate --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var typeOpt = CommandOptionFactory.CreateStringOption("--type", "Class containing the method");
        var methodOpt = CommandOptionFactory.CreateMethodOption("--method", "Method name to add null checks to");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("add-null-checks", "Insert ArgumentNullException.ThrowIfNull guards for reference-type parameters");
        cmd.AddAlias("anc");
        cmd.AddAll(typeOpt, methodOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (type, method, solution, dryRun) =>
        {
            await ExecuteAsync(type, method, solution, dryRun);
        }, typeOpt, methodOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(string typeName, string methodName, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
            await AddNullChecksService.RunAsync(sln, typeName, methodName, dryRun);
    }
}
