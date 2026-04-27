using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class FixNamespaceCommand : IRtCommand
{
    private FixNamespaceCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "fix-namespace",
        Mutates: true,
        JsonOutput: false,
        Category: "Mutation",
        Aliases: ["fns"],
        WhenToUse: "Correcting a file's namespace declaration to match its folder path after moving files, or when a file was created in the wrong location. Also updates any using directives in other files that imported the old namespace.",
        Examples:
        [
            "rt fix-namespace --file src/Services/BookingService.cs --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt fns --file src/RoslynTools/Commands/RenameCommand.cs --dry-run",
            "rt fix-namespace --file src/Domain/Order.cs --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var fileOpt = new Option<string>("--file", "Path to the .cs file to fix") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("fix-namespace", "Align a file's namespace declaration to its folder path");
        cmd.AddAlias("fns");
        cmd.AddAll(fileOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (file, solution, dryRun) =>
        {
            await FixNamespaceService.ExecuteAsync(file, solution, dryRun);
        }, fileOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static Task ExecuteAsync(string filePath, string? solutionPath, bool dryRun) =>
        FixNamespaceService.ExecuteAsync(filePath, solutionPath, dryRun);
}
