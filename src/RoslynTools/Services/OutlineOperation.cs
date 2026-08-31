using System.Diagnostics;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
///     Shared outline application operation. It owns target dispatch and validation so CLI
///     and MCP cannot drift between file, directory, glob, namespace, and regex behavior.
/// </summary>
internal sealed class OutlineOperation
    : CommandApplicationPort<OutlineRequest, RtResultEnvelope<OutlineResult>>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly OutlineService _outline;

    public OutlineOperation(IWorkspaceSessionFactory sessionFactory, OutlineService outline)
    {
        _sessionFactory = sessionFactory;
        _outline = outline;
    }

    public override ApplicationCommandDefinition Definition => OutlineCommandDefinition.Definition;

    public override async Task<Result<RtResultEnvelope<OutlineResult>, RtError>> ExecuteAsync(
        OutlineRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Target is null && request.Regex is null)
            return Result<RtResultEnvelope<OutlineResult>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "Specify a target (file, directory, glob, or namespace) or regex pattern."));

        var stopwatch = Stopwatch.StartNew();
        var (workspace, solution) =
            await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completeness = await WorkspaceCompleteness.AssessSemanticAsync(
                solution,
                cancellationToken);
            IReadOnlyList<object> items;

            if (request.Regex is not null)
                items = await _outline.OutlineRegexAsync(
                    solution,
                    request.Regex,
                    request.Summary,
                    request.WithLocations);
            else
                items = await LoadTargetAsync(solution, request, cancellationToken);

            var totalItems = items.Count;
            var maximumItems = Definition.Product.Limits.MaximumItems;
            var boundedItems = items.Take(maximumItems).ToArray();
            var result = ToResult(boundedItems, request.Summary);
            stopwatch.Stop();
            return Result<RtResultEnvelope<OutlineResult>, RtError>.Ok(CreateEnvelope(
                result,
                solution,
                completeness,
                totalItems,
                boundedItems.Length,
                stopwatch.ElapsedMilliseconds));
        }
    }

    private async Task<IReadOnlyList<object>> LoadTargetAsync(
        Solution solution,
        OutlineRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = request.Target!;
        if (target.EndsWith(".*", StringComparison.Ordinal)
            && !target.Contains('/')
            && !target.Contains('\\'))
            return await _outline.OutlineNamespaceAsync(
                solution, target, request.Summary, request.WithLocations);
        if (target.Contains('*') || target.Contains('?'))
            return await _outline.OutlineGlobAsync(
                solution, target, request.Summary, request.WithLocations);
        if (Directory.Exists(target))
            return await _outline.OutlineDirectoryAsync(
                solution, target, request.Summary, request.WithLocations);
        if (target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || Path.Exists(target))
            return await _outline.OutlineFileAsync(
                solution, target, request.Summary, request.WithLocations);
        return await _outline.OutlineNamespaceAsync(
            solution, target, request.Summary, request.WithLocations);
    }

    private static RtResultEnvelope<OutlineResult> CreateEnvelope(
        OutlineResult data,
        Solution solution,
        WorkspaceCompleteness completeness,
        int totalItems,
        int returnedItems,
        long elapsedMilliseconds)
    {
        var truncated = returnedItems < totalItems;
        var limitations = new List<string>();
        if (completeness.State != WorkspaceCompletenessState.Complete)
            limitations.Add("The workspace is partial; omitted or failed semantic inputs may change the outline.");
        if (truncated)
            limitations.Add("The result reached the item limit; no safe continuation snapshot is available yet.");

        var solutionPath = solution.FilePath;
        var repositoryRoot = solutionPath is null ? null : Path.GetDirectoryName(solutionPath);
        return new RtResultEnvelope<OutlineResult>(
            "rt.result.v1",
            "outline",
            completeness.State == WorkspaceCompletenessState.Complete ? "Succeeded" : "Partial",
            new RtSourceIdentity(repositoryRoot, solutionPath, Revision: null),
            new RtWorkspaceProvenance(
                completeness.State.ToString(),
                solution.ProjectIds.Count,
                completeness.Diagnostics),
            data,
            new RtConfidence(
                completeness.State == WorkspaceCompletenessState.Complete ? "High" : "Limited",
                "Roslyn semantic workspace load",
                [$"Loaded {solution.ProjectIds.Count} project(s)."],
                limitations),
            completeness.Diagnostics.Select((message, index) =>
                new RtStructuredDiagnostic($"workspace.{index + 1}", "Error", message)).ToArray(),
            limitations,
            OutlineCommandDefinition.Definition.SuggestedTools
                .Select(action => new RtNextAction(action.ToolName, action.WhenToUse))
                .ToArray(),
            new RtResultSafety(["RepositoryRead"], false, false),
            new RtTiming(elapsedMilliseconds),
            new RtPaging(totalItems, returnedItems, truncated, ContinuationToken: null));
    }

    private static OutlineResult ToResult(IReadOnlyList<object> items, bool summary)
    {
        if (!summary)
            return new OutlineResult(items.Cast<OutlineTypeResult>().ToList(), null);

        var summaries = items
            .Cast<IReadOnlyDictionary<string, object?>>()
            .Select(static item => new OutlineSummaryResult(
                (string)item["fqn"]!,
                (string)item["kind"]!,
                (int)item["lineFrom"]!,
                (int)item["lineTo"]!,
                item.TryGetValue("description", out var description) ? (string?)description : null))
            .ToList();
        return new OutlineResult(null, summaries);
    }
}
