using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

internal sealed record FindDuplicatesRequest(
    string? Solution = null,
    [property: JsonPropertyName("min-statements")] int MinStatements = 3,
    [property: JsonPropertyName("min-occurrences")] int MinOccurrences = 2,
    string? Scope = null,
    [property: JsonPropertyName("include-tests")] bool IncludeTests = false,
    [property: JsonPropertyName("max-statements")] int MaxStatements = 40);

internal sealed record FindDuplicatesReport(
    IReadOnlyList<DuplicateGroup> Groups,
    string? SolutionDirectory,
    string? SolutionPath);

internal sealed class FindDuplicatesOperation(
    IWorkspaceSessionFactory sessionFactory,
    FindDuplicatesService findDuplicates)
    : CommandApplicationPort<FindDuplicatesRequest, FindDuplicatesReport>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;
    private readonly FindDuplicatesService _findDuplicates = findDuplicates;

    public override ApplicationCommandDefinition Definition => FindDuplicatesCommandDefinition.Definition;

    public override async Task<Result<FindDuplicatesReport, RtError>> ExecuteAsync(
        FindDuplicatesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.MinStatements < 1 || request.MinOccurrences < 2 || request.MaxStatements < request.MinStatements)
            return Result<FindDuplicatesReport, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "Duplicate thresholds must be positive, with max-statements at least min-statements."));

        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var groups = await _findDuplicates.RunAsync(
                solution,
                new FindDuplicatesOptions(request.MinStatements, request.MinOccurrences, request.Scope,
                    request.IncludeTests, request.MaxStatements));
            var directory = solution.FilePath is null ? null : Path.GetDirectoryName(solution.FilePath);
            return Result<FindDuplicatesReport, RtError>.Ok(new FindDuplicatesReport(groups, directory, solution.FilePath));
        }
    }
}

internal static class FindDuplicatesCommandDefinition
{
    internal const string Name = "find_duplicates";
    internal static RtCommandInfo Info { get; } = new(
        "find-duplicates", false, true, "analyze",
        Aliases: ["fd"],
        WhenToUse:
        "Use this instead of Grep when discovering repeated code blocks (exact clones) across a solution before an extract-method pass. Use --min-statements to tune noise vs. signal, --min-occurrences to target the most-duplicated blocks, and --scope to focus on one subsystem.",
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
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]);
    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info, typeof(FindDuplicatesRequest), typeof(FindDuplicatesReport), true,
        CommandSafety.ReadOnly, CommandExecutionPlacement.DaemonPreferred, []);
}
