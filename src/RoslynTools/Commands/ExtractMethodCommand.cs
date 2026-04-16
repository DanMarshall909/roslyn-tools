using System.CommandLine;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal static class ExtractMethodCommand
{
    internal static Command Build()
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
