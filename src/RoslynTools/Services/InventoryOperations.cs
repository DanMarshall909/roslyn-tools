using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Services.Results;

namespace RoslynTools.Services;

internal sealed record FindUnusedRequest(
    string? Solution = null,
    string Scope = "all");

internal sealed class FindUnusedOperation(IWorkspaceSessionFactory sessionFactory, FindUnusedService service)
    : CommandApplicationPort<FindUnusedRequest, FindUnusedAnalysisResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;
    private readonly FindUnusedService _service = service;
    public override ApplicationCommandDefinition Definition => FindUnusedCommandDefinition.Definition;
    public override async Task<Result<FindUnusedAnalysisResult, RtError>> ExecuteAsync(FindUnusedRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Scope is not ("all" or "types" or "methods"))
            return Result<FindUnusedAnalysisResult, RtError>.Err(new RtError(RtErrorKind.InvalidInput, "--scope must be one of: all, types, methods."));
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        cancellationToken.ThrowIfCancellationRequested();
        using (workspace)
        {
            var symbols = await _service.AnalyzeAsync(solution, request.Scope);
            var result = symbols.Select(s => new UnusedSymbolResult(
                s.Kind,
                s.Name,
                s.FilePath ?? string.Empty,
                s.Line,
                s.Confidence,
                s.Reason,
                s.Confidence.Equals(UnusedConfidence.High, StringComparison.OrdinalIgnoreCase)
                    ? FixCatalog.ForUnusedSymbol(s.Kind, s.Name, solution.FilePath)
                    : [])).ToList();
            return Result<FindUnusedAnalysisResult, RtError>.Ok(
                new FindUnusedAnalysisResult(result));
        }
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
