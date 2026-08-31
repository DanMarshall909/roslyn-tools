using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

internal sealed record SearchSymbolsRequest(
    [property: JsonRequired]
    [property: CommandPropertyDescription("Exact or partial symbol name, FQN, or signature text")]
    string Query,
    string? Solution = null,
    string? Kind = null,
    string? Project = null,
    string? Namespace = null,
    string? Accessibility = null,
    int? GenericArity = null,
    string? Signature = null,
    int Limit = 50,
    bool Fuzzy = false);

internal sealed record SymbolCandidate(
    string SymbolId,
    string Kind,
    string Name,
    string Fqn,
    string Signature,
    string Project,
    string Namespace,
    string Accessibility,
    int GenericArity,
    string? DeclaringPath,
    int Rank);

internal sealed record SearchSymbolsResult(IReadOnlyList<SymbolCandidate> Items);

internal sealed class SearchSymbolsOperation(
    IWorkspaceSessionFactory sessions,
    ISymbolCandidateService candidates)
    : CommandApplicationPort<SearchSymbolsRequest, RtResultEnvelope<SearchSymbolsResult>>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessions = sessions;
    private readonly ISymbolCandidateService _candidates = candidates;

    public override ApplicationCommandDefinition Definition => SearchSymbolsCommandDefinition.Definition;

    public override async Task<Result<RtResultEnvelope<SearchSymbolsResult>, RtError>> ExecuteAsync(
        SearchSymbolsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return Result<RtResultEnvelope<SearchSymbolsResult>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "A symbol search query is required."));
        if (request.Limit <= 0 || request.Limit > Definition.Product.Limits.MaximumItems)
            return Result<RtResultEnvelope<SearchSymbolsResult>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"Limit must be between 1 and {Definition.Product.Limits.MaximumItems}."));
        if (request.GenericArity < 0)
            return Result<RtResultEnvelope<SearchSymbolsResult>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "Generic arity cannot be negative."));
        var stopwatch = Stopwatch.StartNew();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            var completeness = await WorkspaceCompleteness.AssessSemanticAsync(
                solution,
                cancellationToken);
            var sourceRevision = await SolutionRevision.ComputeAsync(solution, cancellationToken);
            var ordered = (await _candidates.SearchAsync(
                    solution,
                    new SymbolCandidateQuery(
                        request.Query,
                        request.Kind,
                        request.Project,
                        request.Namespace,
                        request.Accessibility,
                        request.GenericArity,
                        request.Signature,
                        request.Fuzzy,
                        sourceRevision),
                    cancellationToken))
                .Select(candidate => candidate.Candidate)
                .ToArray();
            var items = ordered.Take(request.Limit).ToArray();
            stopwatch.Stop();
            return Result<RtResultEnvelope<SearchSymbolsResult>, RtError>.Ok(CreateEnvelope(
                solution,
                completeness,
                request.Query,
                sourceRevision,
                items,
                ordered.Length,
                stopwatch.ElapsedMilliseconds));
        }
    }

    private static RtResultEnvelope<SearchSymbolsResult> CreateEnvelope(
        Solution solution,
        WorkspaceCompleteness completeness,
        string query,
        string sourceRevision,
        SymbolCandidate[] items,
        int total,
        long elapsedMilliseconds)
    {
        var truncated = items.Length < total;
        var limitations = new List<string>();
        if (completeness.State != WorkspaceCompletenessState.Complete)
            limitations.Add("The workspace is partial; candidates from failed or omitted inputs may be absent.");
        if (truncated)
            limitations.Add("The result reached the item limit; narrow the filters because no integrity-protected revision-safe continuation snapshot is available yet.");
        var solutionPath = solution.FilePath;
        return new RtResultEnvelope<SearchSymbolsResult>(
            "rt.result.v1",
            "search-symbols",
            completeness.State == WorkspaceCompletenessState.Complete ? "Succeeded" : "Partial",
            new RtSourceIdentity(
                solutionPath is null ? null : Path.GetDirectoryName(solutionPath),
                solutionPath,
                sourceRevision),
            new RtWorkspaceProvenance(
                completeness.State.ToString(),
                solution.ProjectIds.Count,
                completeness.Diagnostics),
            new SearchSymbolsResult(items),
            new RtConfidence(
                completeness.State == WorkspaceCompletenessState.Complete ? "High" : "Limited",
                "Deterministic source-symbol ranking",
                [$"Matched query '{query}' against names, FQNs, and signatures."],
                limitations),
            completeness.Diagnostics.Select((message, index) =>
                new RtStructuredDiagnostic($"workspace.{index + 1}", "Error", message)).ToArray(),
            limitations,
            items.Length == 1
                ? [new RtNextAction("describe", "Inspect the selected candidate's focused signature.")]
                : [],
            new RtResultSafety(["RepositoryRead"], false, false),
            new RtTiming(elapsedMilliseconds),
            new RtPaging(
                total,
                items.Length,
                truncated,
                ContinuationToken: null));
    }
}

internal static class SearchSymbolsCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "search-symbols",
        false,
        true,
        "inspect",
        "Use this when a symbol name is unknown, partial, or ambiguous and you need deterministic typed candidates before describe, source, relationships, or change planning.",
        [
            "rt inspect search-symbols ICommandApplicationPort --kind NamedType --solution RoslynTools.slnx --json",
            "rt inspect search-symbols Execute --project RoslynTools --limit 20 --solution RoslynTools.slnx --json"
        ],
        SeeAlso: ["describe", "source", "relationships"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(SearchSymbolsRequest),
        typeof(RtResultEnvelope<SearchSymbolsResult>),
        mcpVisible: true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.InProcess,
        []);
}
