using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class ExtractClassCommand : IRtCommand
{
    private ExtractClassCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "extract-class",
        Mutates: true,
        JsonOutput: false,
        Category: "refactor",
        Aliases: ["ec"],
        WhenToUse: "Moving a named set of members out of an oversized class into a new dedicated class file. When --static is not set, the command also wires the new class into the source class as a constructor-injected dependency, making it immediately usable without manual plumbing.",
        Examples:
        [
            "rt refactor extract-class --type AdminController --members ExportCsv,ImportCsv --new-type CsvController --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor ec --type ReportService --members BuildReport,FormatReport --new-type ReportFormatter --static --dry-run",
            "rt refactor extract-class --type ProductService --members ValidateProduct --new-type ProductValidator --solution RoslynTools.slnx --output-dir src/Validators",
        ]
    );

    public static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Source class name") { IsRequired = true };
        var membersOpt = new Option<string>("--members", "Comma-separated member names to extract") { IsRequired = true };
        var newTypeOpt = new Option<string>("--new-type", "Name for the new class") { IsRequired = true };
        var staticOpt = new Option<bool>("--static", "Make the new class static (no injection)");
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var outputDirOpt = new Option<string?>("--output-dir", "Directory for new file");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("extract-class", "Move named members from a class into a new class file");
        cmd.AddAlias("ec");
        cmd.AddAll(typeOpt, membersOpt, newTypeOpt, staticOpt, solutionOpt, outputDirOpt, dryRunOpt);

        cmd.SetHandler(async (type, members, newType, isStatic, solution, outputDir, dryRun) =>
        {
            await ExecuteAsync(type, members, newType, isStatic, solution, outputDir, dryRun);
        }, typeOpt, membersOpt, newTypeOpt, staticOpt, solutionOpt, outputDirOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(
        string typeName,
        string members,
        string newTypeName,
        bool isStatic,
        string? solutionPath,
        string? outputDir,
        bool dryRun)
    {
        var (workspace, solution) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            await ExtractClassService.ExecuteAsync(solution, typeName, members, newTypeName, isStatic, outputDir, dryRun);
        }
    }
}
