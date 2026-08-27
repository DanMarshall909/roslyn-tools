using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Fixes namespace issues in the current file
/// </summary>
internal sealed class FixNamespaceCommand(FixNamespaceService fixNamespace) : IRtCommand, IRefactorCommand
{
    private readonly FixNamespaceService _fixNamespace = fixNamespace;
    public static RtCommandInfo Info { get; } = new(
        "fix-namespace",
        true,
        false,
        "refactor",
        Aliases: ["fns"],
        WhenToUse:
        "Correcting a file's namespace declaration to match its folder path after moving files, or when a file was created in the wrong location. Also updates any using directives in other files that imported the old namespace.",
        Examples:
        [
            "rt refactor fix-namespace --file src/Services/BookingService.cs --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor fns --file src/RoslynTools/Commands/RenameCommand.cs --dry-run",
            "rt refactor fix-namespace --file src/Domain/Order.cs --solution RoslynTools.slnx"
        ]
    );

    public Command Build()
    {
        var fileOpt = CommandOptionFactory.CreateStringOption("--file", "Path to the .cs file to fix");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("fix-namespace", "Align a file's namespace declaration to its folder path");
        cmd.AddAlias("fns");
        cmd.AddAll(fileOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(
            async (file, solution, dryRun) =>
            {
                await _fixNamespace.ExecuteAsync(file, solution, dryRun);
            }, fileOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    /// <summary>
    /// Executes the FixNamespaceCommand with async parameters
    /// </summary>
    internal Task ExecuteAsync(string filePath, string? solutionPath, bool dryRun)
    {
        return _fixNamespace.ExecuteAsync(filePath, solutionPath, dryRun);
    }
}
