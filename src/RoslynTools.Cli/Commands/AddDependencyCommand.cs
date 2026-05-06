using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class AddDependencyCommand : IRtCommand
{
    private AddDependencyCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "add-dependency",
        Mutates: true,
        JsonOutput: false,
        Category: "refactor",
        Aliases: ["ad"],
        WhenToUse: "Injecting a new constructor dependency into a class without manually editing the constructor and field declarations. Prefer this over a manual edit when the class has multiple constructors or when you want to be confident the field naming convention (_camelCase) is applied consistently.",
        Examples:
        [
            "rt refactor add-dependency --type BookingService --dependency IEmailService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor ad --type OrderController --dependency IPaymentGateway --field _payment --dry-run",
            "rt refactor add-dependency --type ReportGenerator --dependency IRepository --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Target class name") { IsRequired = true };
        var depOpt = new Option<string>("--dependency", "Dependency type to inject (e.g. IEmailService)") { IsRequired = true };
        var fieldOpt = CommandOptionFactory.CreateFieldOption("--field", "Field name override (default: derived from type)");
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("add-dependency", "Inject a constructor dependency into a class");
        cmd.AddAlias("ad");
        cmd.AddAll(typeOpt, depOpt, fieldOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (type, dep, field, solution, dryRun) =>
        {
            await ExecuteAsync(type, dep, field, solution, dryRun);
        }, typeOpt, depOpt, fieldOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(string typeName, string depType, string? fieldName, string? solutionPath, bool dryRun)
    {
        var (workspace, solution) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            await new AddDependencyService().RunAsync(solution, typeName, depType, fieldName, dryRun);
        }
    }

    // Forwarding stub kept for backwards compatibility
    internal static Task ExecuteCoreAsync(Microsoft.CodeAnalysis.Solution solution, string typeName, string depType, string? fieldName, bool dryRun) =>
        new AddDependencyService().RunAsync(solution, typeName, depType, fieldName, dryRun);
}
