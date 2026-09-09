using System.Diagnostics;
using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Services.Results;

namespace RoslynTools.Services;

internal sealed record FindUnusedRequest(
    string? Solution = null,
    string Scope = "all");

internal sealed class FindUnusedOperation(IWorkspaceSessionFactory sessionFactory, FindUnusedService service)
    : CommandApplicationPort<FindUnusedRequest, RtResultEnvelope<FindUnusedAnalysisResult>>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;
    private readonly FindUnusedService _service = service;
    public override ApplicationCommandDefinition Definition => FindUnusedCommandDefinition.Definition;
    public override async Task<Result<RtResultEnvelope<FindUnusedAnalysisResult>, RtError>> ExecuteAsync(
        FindUnusedRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        if (request.Scope is not ("all" or "types" or "methods"))
            return Result<RtResultEnvelope<FindUnusedAnalysisResult>, RtError>.Err(new RtError(RtErrorKind.InvalidInput, "--scope must be one of: all, types, methods."));
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        cancellationToken.ThrowIfCancellationRequested();
        using (workspace)
        {
            var symbols = await _service.AnalyzeAsync(solution, request.Scope, cancellationToken);
            // Finalize after reference analysis so late workspace/generator failures are retained.
            var completeness = await WorkspaceCompleteness.AssessSemanticAsync(solution, cancellationToken);
            var isComplete = completeness.State == WorkspaceCompletenessState.Complete;
            var result = symbols.Select(s => new UnusedSymbolResult(
                s.Kind,
                s.Name,
                s.FilePath ?? string.Empty,
                s.Line,
                isComplete ? s.Confidence : UnusedConfidence.Low,
                isComplete
                    ? s.Reason
                    : $"{s.Reason} The semantic workspace is {completeness.State}, so reference absence is not proof.",
                isComplete && s.Confidence.Equals(UnusedConfidence.High, StringComparison.OrdinalIgnoreCase)
                    ? FixCatalog.ForUnusedSymbol(s.Kind, s.Name, solution.FilePath)
                    : [])).ToList();
            var limitations = new List<string>
            {
                "Source analysis cannot prove reflection, runtime activation, generated use, or external consumers are absent."
            };
            if (!isComplete)
                limitations.Add("The workspace is incomplete; findings are advisory and cannot offer deletion advice.");
            if (completeness.Acquisition is { } acquisition)
                limitations.AddRange(acquisition.Limitations);
            return Result<RtResultEnvelope<FindUnusedAnalysisResult>, RtError>.Ok(
                new RtResultEnvelope<FindUnusedAnalysisResult>(
                    "rt.result.v1",
                    "find-unused",
                    isComplete ? "Succeeded" : completeness.State.ToString(),
                    new RtSourceIdentity(
                        completeness.Acquisition?.Provenance.RepositoryRoot,
                        solution.FilePath,
                        await SolutionRevision.ComputeAsync(solution, cancellationToken)),
                    new RtWorkspaceProvenance(
                        completeness.State.ToString(),
                        solution.ProjectIds.Count,
                        completeness.Diagnostics,
                        completeness.Inputs,
                        completeness.RequestedProjects,
                        completeness.FailedProjects,
                        completeness.CompilerVersion,
                        completeness.EvaluatorPolicy,
                        completeness.Acquisition?.Provenance,
                        completeness.Acquisition?.Manifest.DiscoveryState.ToString()),
                    new FindUnusedAnalysisResult(result),
                    new RtConfidence(
                        isComplete ? "High" : "Limited",
                        "Roslyn semantic reference analysis",
                        [$"Evaluated {solution.ProjectIds.Count} project(s) for unused {request.Scope}."],
                        limitations),
                    completeness.StructuredDiagnostics.Select(ToStructuredDiagnostic).ToArray(),
                    limitations,
                    [],
                    new RtResultSafety(["RepositoryRead"], false, false),
                    new RtTiming(stopwatch.ElapsedMilliseconds),
                    new RtPaging(result.Count, result.Count, false, null)));
        }
    }

    private static RtStructuredDiagnostic ToStructuredDiagnostic(WorkspaceAcquisitionDiagnostic diagnostic)
    {
        var context = new Dictionary<string, string>
        {
            ["origin"] = diagnostic.Origin,
            ["completenessImpact"] = diagnostic.Impact.ToString()
        };
        if (diagnostic.InputId is { } inputId)
            context["inputId"] = inputId;
        if (diagnostic.ProjectPath is { } projectPath)
            context["projectPath"] = projectPath;
        return new RtStructuredDiagnostic(diagnostic.Id, diagnostic.Severity.ToString(), diagnostic.Message, context);
    }
}

internal sealed record FindUndocumentedRequest(
    string? Solution = null,
    bool Members = false,
    string? Scope = null,
    string Level = "public");

internal sealed class FindUndocumentedOperation(IWorkspaceSessionFactory sessionFactory, FindUndocumentedService service)
    : CommandApplicationPort<FindUndocumentedRequest, FindUndocumentedAnalysisResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;
    private readonly FindUndocumentedService _service = service;
    public override ApplicationCommandDefinition Definition => FindUndocumentedCommandDefinition.Definition;
    public override async Task<Result<FindUndocumentedAnalysisResult, RtError>> ExecuteAsync(FindUndocumentedRequest request, CancellationToken cancellationToken = default)
    {
        var levels = request.Level.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (levels.Length == 0 || levels.Any(level => level is not ("all" or "public" or "internal" or "private" or "protected")))
            return Result<FindUndocumentedAnalysisResult, RtError>.Err(new RtError(RtErrorKind.InvalidInput, "--level must contain only: public, internal, protected, private, all."));
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        cancellationToken.ThrowIfCancellationRequested();
        using (workspace)
        {
            var symbols = await _service.AnalyzeAsync(solution, request.Members, request.Scope, FindUndocumentedService.ParseAccessibilityLevels(levels));
            var result = symbols.Select(s => new UndocumentedSymbolResult(
                s.Fqn, s.Signature, s.Kind, s.FilePath, s.Line)).ToList();
            return Result<FindUndocumentedAnalysisResult, RtError>.Ok(
                new FindUndocumentedAnalysisResult(
                    result,
                    FixCatalog.ForUndocumented(solution.FilePath)));
        }
    }
}

internal sealed record StaticClassInventoryRequest(string? Solution = null);

internal sealed class StaticClassInventoryOperation(IWorkspaceSessionFactory sessionFactory, StaticClassInventoryService service)
    : CommandApplicationPort<StaticClassInventoryRequest, StaticClassInventoryAnalysisResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;
    private readonly StaticClassInventoryService _service = service;
    public override ApplicationCommandDefinition Definition => StaticClassInventoryCommandDefinition.Definition;
    public override async Task<Result<StaticClassInventoryAnalysisResult, RtError>> ExecuteAsync(StaticClassInventoryRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        cancellationToken.ThrowIfCancellationRequested();
        using (workspace)
        {
            var report = await _service.AnalyzeAsync(solution);
            return Result<StaticClassInventoryAnalysisResult, RtError>.Ok(new StaticClassInventoryAnalysisResult(report.TotalStaticClasses, report.Classes));
        }
    }
}
