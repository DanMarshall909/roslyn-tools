using System.CommandLine;
using System.Text.Json;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class FindPatternCommand : IRtCommand
{
    private FindPatternCommand() { }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static RtCommandInfo Info { get; } = new(
        Name: "find-pattern",
        Mutates: false,
        JsonOutput: true,
        Category: "Navigation & inspection",
        Aliases: ["fp"],
        WhenToUse: "Find every location in the solution that structurally matches a given line range — same SyntaxKind tree shape, only identifier and literal values differ. Useful for spotting duplicated logic even when variable names differ.",
        Examples:
        [
            "rt find-pattern --file src/Services/BookingService.cs --start-line 42 --end-line 50 --solution RoslynTools.slnx",
            "rt fp --file src/Controllers/AdminController.cs --start-line 110 --end-line 125 --scope RoslynTools.Core --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var fileOpt = new Option<string>("--file", "Path to the .cs file containing the pattern.") { IsRequired = true };
        var startLineOpt = new Option<int>("--start-line", "First line of the pattern range (1-based).") { IsRequired = true };
        var endLineOpt = new Option<int>("--end-line", "Last line of the pattern range (1-based, inclusive).") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx. Auto-detected if omitted.");
        var scopeOpt = new Option<string?>("--scope", "Filter matches to documents in this namespace prefix or project name.");

        var cmd = new Command("find-pattern", "Find all solution locations structurally matching a given statement range.");
        cmd.AddAlias("fp");
        cmd.AddAll(fileOpt, startLineOpt, endLineOpt, solutionOpt, scopeOpt);

        cmd.SetHandler(async (file, startLine, endLine, solution, scope) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                Console.Error.WriteLine($"Searching for pattern at {file}:{startLine}–{endLine}…");
                List<PatternMatch> matches;
                try
                {
                    matches = await PatternMatcher.FindMatchesAsync(sln, file, startLine, endLine, scope);
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    Environment.ExitCode = 1;
                    return;
                }

                var output = new { matches };
                Console.WriteLine(JsonSerializer.Serialize(output, JsonOptions));
            }
        }, fileOpt, startLineOpt, endLineOpt, solutionOpt, scopeOpt);

        return cmd;
    }
}
