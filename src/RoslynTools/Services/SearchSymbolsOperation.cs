using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
    bool Fuzzy = false,
    string? ContinuationToken = null);

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
        var continuation = ParseContinuation(request);
        if (continuation.IsError)
            return Result<RtResultEnvelope<SearchSymbolsResult>, RtError>.Err(continuation.Error!);

        var stopwatch = Stopwatch.StartNew();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            var completeness = await WorkspaceCompleteness.AssessSemanticAsync(
                solution,
                cancellationToken);
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
                        request.Fuzzy),
                    cancellationToken))
                .Select(candidate => candidate.Candidate)
                .ToArray();
            var items = ordered.Skip(continuation.Offset).Take(request.Limit).ToArray();
            stopwatch.Stop();
            return Result<RtResultEnvelope<SearchSymbolsResult>, RtError>.Ok(CreateEnvelope(
                solution,
                completeness,
                request.Query,
                request,
                continuation.Offset,
                items,
                ordered.Length,
                stopwatch.ElapsedMilliseconds));
        }
    }

    private static Continuation ParseContinuation(SearchSymbolsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ContinuationToken))
            return new Continuation(0, null);

        var parts = request.ContinuationToken.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3
            || !string.Equals(parts[0], "v1", StringComparison.Ordinal)
            || !int.TryParse(parts[1], out var offset)
            || offset < 0
            || !string.Equals(parts[2], QueryFingerprint(request), StringComparison.Ordinal))
        {
            return new Continuation(0, new RtError(
                RtErrorKind.InvalidInput,
                "The continuation token is invalid or belongs to a different symbol query."));
        }

        return new Continuation(offset, null);
    }

    private static string CreateContinuation(SearchSymbolsRequest request, int offset) =>
        $"v1.{offset}.{QueryFingerprint(request)}";

    private static string QueryFingerprint(SearchSymbolsRequest request)
    {
        var canonical = string.Join(
            '\u001f',
            request.Query,
            request.Kind,
            request.Project,
            request.Namespace,
            request.Accessibility,
            request.GenericArity,
            request.Signature,
            request.Fuzzy);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16];
    }

    private static RtResultEnvelope<SearchSymbolsResult> CreateEnvelope(
        Solution solution,
        WorkspaceCompleteness completeness,
        string query,
        SearchSymbolsRequest request,
        int offset,
        SymbolCandidate[] items,
        int total,
        long elapsedMilliseconds)
    {
        var returnedThrough = offset + items.Length;
        var truncated = returnedThrough < total;
        var limitations = new List<string>();
        if (completeness.State != WorkspaceCompletenessState.Complete)
            limitations.Add("The workspace is partial; candidates from failed or omitted inputs may be absent.");
        if (truncated)
            limitations.Add("The result reached the item limit; use the continuation token against an unchanged workspace or narrow the filters.");
        if (offset > 0 || truncated)
            limitations.Add("Continuation tokens bind query options but not a source revision; restart the search if the workspace changes.");
        var solutionPath = solution.FilePath;
        return new RtResultEnvelope<SearchSymbolsResult>(
            "rt.result.v1",
            "search-symbols",
            completeness.State == WorkspaceCompletenessState.Complete ? "Succeeded" : "Partial",
            new RtSourceIdentity(
                solutionPath is null ? null : Path.GetDirectoryName(solutionPath),
                solutionPath,
                Revision: null),
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
                truncated ? CreateContinuation(request, returnedThrough) : null));
    }

    private sealed record Continuation(int Offset, RtError? Error)
    {
        public bool IsError => Error is not null;
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
