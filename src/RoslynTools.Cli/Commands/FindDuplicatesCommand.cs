using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Identifies duplicate symbols in the current file
/// </summary>
internal sealed class FindDuplicatesCommand : IRtCommand, IStructuredOutputCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly FindDuplicatesService _findDuplicates;
    public FindDuplicatesCommand(IWorkspaceSessionFactory sessionFactory, FindDuplicatesService findDuplicates)
    {
        _findDuplicates = findDuplicates;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "find-duplicates",
        false,
        true,
        "analyze",
        Aliases: ["fd"],
        WhenToUse:
        "Discovering repeated code blocks (exact clones) across a solution before an extract-method pass. Use --min-statements to tune noise vs. signal, --min-occurrences to target the most-duplicated blocks, and --scope to focus on one subsystem.",
        Examples:
        [
            "rt analyze find-duplicates --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt analyze fd --min-statements 5 --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt analyze find-duplicates --min-statements 4 --min-occurrences 3 --solution RoslynTools.slnx",
            "rt analyze find-duplicates --scope MyApp.Services --solution RoslynTools.slnx",
            "rt analyze find-duplicates --include-tests --solution RoslynTools.slnx",
            "rt analyze find-duplicates --max-statements 20 --solution RoslynTools.slnx",
            "rt analyze find-duplicates --json --solution RoslynTools.slnx"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]
    );

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var minStatementsOpt = new Option<int>("--min-statements", () => 3,
            "Minimum number of statements in a duplicate window");
        var minOccurrencesOpt =
            new Option<int>("--min-occurrences", () => 2, "Minimum number of occurrences to report a group");
        var scopeOpt = new Option<string?>("--scope", "Project name or namespace prefix to restrict search to");
        var includeTestsOpt =
            new Option<bool>("--include-tests", () => false, "Include test projects (excluded by default)");
        var maxStatementsOpt = new Option<int>("--max-statements", () => 40, "Hard cap on duplicate window size");

        var cmd = new Command("find-duplicates", "Report exact-clone code blocks repeated across the solution");
        cmd.AddAlias("fd");
        cmd.Add(solutionOpt);
        cmd.Add(minStatementsOpt);
        cmd.Add(minOccurrencesOpt);
        cmd.Add(scopeOpt);
        cmd.Add(includeTestsOpt);
        cmd.Add(maxStatementsOpt);

        cmd.SetHandler(async (solution, minStatements, minOccurrences, scope, includeTests, maxStatements) =>
        {
            var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solution);
            using (workspace)
            {
                var options =
                    new FindDuplicatesOptions(minStatements, minOccurrences, scope, includeTests, maxStatements);
                var groups = await _findDuplicates.RunAsync(sln, options);

                var solutionDir = sln.FilePath is not null
                    ? Path.GetDirectoryName(sln.FilePath)
                    : null;

                var result = FindDuplicatesResult.FromGroups(groups, solutionDir);
                RtResultRenderer.WriteOutput(RtResult<FindDuplicatesResult>.Ok(result));
            }
        }, solutionOpt, minStatementsOpt, minOccurrencesOpt, scopeOpt, includeTestsOpt, maxStatementsOpt);

        return cmd;
    }
}
