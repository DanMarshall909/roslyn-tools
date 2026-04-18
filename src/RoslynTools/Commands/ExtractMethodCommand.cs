using System.CommandLine;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class ExtractMethodCommand : IRtCommand
{
    private ExtractMethodCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "extract-method",
        Mutates: true,
        JsonOutput: false,
        Category: "Mutation",
        Aliases: ["em"],
        WhenToUse: "Pulling a consecutive range of statements out of a long method body into a new private method. Specify the line range from the source file; the command infers parameter and return types from context. Use --after/--before to control insertion point.",
        Examples:
        [
            "rt extract-method src/Services/BookingService.cs ValidateBooking --start-line 42 --end-line 58 --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt em src/Controllers/AdminController.cs BuildCsvRow --start-line 110 --end-line 125 --dry-run",
            "rt extract-method src/RoslynTools/Commands/RenameCommand.cs BuildOptions --start-line 30 --end-line 45 --after Build --solution RoslynTools.slnx",
        ],
        KnownIssues:
        [
            "Invoking via `cmd //c` from bash on Windows crashes during workspace load — see ai-improvements row #13.",
        ]
    );

    public static Command Build()
    {
        var fileArg = new Argument<string>("file", "Path to the .cs file containing the method to extract from.");
        var newNameArg = new Argument<string>("new-name", "Name for the extracted method.");

        var startLineOpt = new Option<int>("--start-line", "First line of the statements to extract (1-based).") { IsRequired = true };
        var endLineOpt = new Option<int>("--end-line", "Last line of the statements to extract (1-based, inclusive).") { IsRequired = true };
        var afterOpt = new Option<string?>("--after", "Insert the new method after this member name. Default: after the containing method.");
        var beforeOpt = new Option<string?>("--before", "Insert the new method before this member name.");
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln file. Auto-detected if omitted.");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk.");

        var cmd = new Command("extract-method", "Extract a range of statements into a new private method.")
        {
            fileArg, newNameArg, startLineOpt, endLineOpt, afterOpt, beforeOpt, solutionOpt, dryRunOpt
        };
        cmd.AddAlias("em");

        cmd.SetHandler(
            async (file, newName, startLine, endLine, after, before, solution, dryRun) =>
            {
                var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
                using (workspace)
                {
                    Console.Error.WriteLine(
                        $"Extracting lines {startLine}–{endLine} of '{file}' → '{newName}'...");

                    Solution newSln;
                    try
                    {
                        newSln = await MethodExtractor.ExtractAsync(
                            sln,
                            file,
                            newName,
                            startLine,
                            endLine,
                            insertAfter: after,
                            insertBefore: before);
                    }
                    catch (InvalidOperationException ex)
                    {
                        Console.Error.WriteLine($"Error: {ex.Message}");
                        Environment.ExitCode = 1;
                        return;
                    }

                    await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun);
                }
            },
            fileArg, newNameArg, startLineOpt, endLineOpt, afterOpt, beforeOpt, solutionOpt, dryRunOpt);

        return cmd;
    }
}
