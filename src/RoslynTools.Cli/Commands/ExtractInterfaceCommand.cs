using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class ExtractInterfaceCommand : IRtCommand
{
    private ExtractInterfaceCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "extract-interface",
        Mutates: true,
        JsonOutput: false,
        Category: "refactor",
        Aliases: ["ei"],
        WhenToUse: "Generating an IXxx interface from an existing class's public members and updating the class declaration to implement it. Use this before injecting a service as a dependency, or when introducing a seam for testing.",
        Examples:
        [
            "rt refactor extract-interface --type BookingService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor ei --type EmailSender --output-dir src/Interfaces --dry-run",
            "rt refactor extract-interface --type HeroApiClient --namespace ProjectX.Core.Abstractions --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Class to extract interface from") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var outputDirOpt = new Option<string?>("--output-dir", "Directory for the new interface file");
        var namespaceOpt = new Option<string?>("--namespace", "Namespace override for the interface");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("extract-interface", "Generate IXxx interface from a class's public members");
        cmd.AddAlias("ei");
        cmd.AddAll(typeOpt, solutionOpt, outputDirOpt, namespaceOpt, dryRunOpt);

        cmd.SetHandler(async (type, solution, outputDir, ns, dryRun) =>
        {
            await ExecuteAsync(type, solution, outputDir, ns, dryRun);
        }, typeOpt, solutionOpt, outputDirOpt, namespaceOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(
        string typeName,
        string? solutionPath,
        string? outputDir,
        string? ns,
        bool dryRun)
    {
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            await new ExtractInterfaceService().RunAsync(sln, typeName, outputDir, ns, dryRun);
        }
    }
}
