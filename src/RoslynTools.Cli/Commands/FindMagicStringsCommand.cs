using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class FindMagicStringsCommand : IRtCommand
{
    private FindMagicStringsCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "find-magic-strings",
        false,
        false,
        "analyze",
        Aliases: ["magic-strings"],
        WhenToUse:
        "Finding repeated string literals that should probably be named constants before cleanup or domain refactoring. Generated files, const string declarations, and .rtignore matches are skipped.",
        Examples:
        [
            "rt analyze find-magic-strings --solution RoslynTools.slnx",
            "rt analyze magic-strings --min-length 4 --min-occurrences 3 --solution C:\\code\\app\\App.sln",
            "rt analyze find-magic-strings --json --solution RoslynTools.slnx"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]
    );

    public static Command Build()
    {
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var minLengthOpt = new Option<int>("--min-length", () => 3, "Minimum string length to report");
        var minOccurrencesOpt = new Option<int>("--min-occurrences", () => 2, "Minimum occurrences of the same value");

        var cmd = new Command("find-magic-strings", "Report repeated string literals");
        cmd.AddAlias("magic-strings");
        cmd.AddAll(solutionOpt, minLengthOpt, minOccurrencesOpt);

        cmd.SetHandler(async (solution, minLength, minOccurrences) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var groups = await RtServiceProvider.Get<MagicStringService>()
                    .AnalyzeAsync(sln, minLength, minOccurrences);
                RtSerializer.Write(MagicStringResult.FromGroups(groups));
            }
        }, solutionOpt, minLengthOpt, minOccurrencesOpt);

        return cmd;
    }
}
