using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Identifies magic string literals in the current file
/// </summary>
internal sealed class FindMagicStringsCommand : IRtCommand, IAnalyzeCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly MagicStringService _magicString;
    public FindMagicStringsCommand(IWorkspaceSessionFactory sessionFactory, MagicStringService magicString)
    {
        _magicString = magicString;
        _sessionFactory = sessionFactory;
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

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var minLengthOpt = new Option<int>("--min-length", () => 3, "Minimum string length to report");
        var minOccurrencesOpt = new Option<int>("--min-occurrences", () => 2, "Minimum occurrences of the same value");

        var cmd = new Command("find-magic-strings", "Report repeated string literals");
        cmd.AddAlias("magic-strings");
        cmd.AddAll(solutionOpt, minLengthOpt, minOccurrencesOpt);

        cmd.SetHandler(async (solution, minLength, minOccurrences) =>
        {
            var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solution);
            using (workspace)
            {
                var groups = await _magicString
                    .AnalyzeAsync(sln, minLength, minOccurrences);
                RtResultRenderer.WriteOutput(RtResult<MagicStringResult>.Ok(MagicStringResult.FromGroups(groups, sln.FilePath)));
            }
        }, solutionOpt, minLengthOpt, minOccurrencesOpt);

        return cmd;
    }
}
