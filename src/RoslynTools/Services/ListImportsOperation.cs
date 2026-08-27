using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for imported-directive analysis.</summary>
internal sealed record ListImportsRequest(
    string? Solution = null,
    [property: JsonPropertyName("kind")] string ImportKind = "all");

/// <summary>One imported directive returned by the analysis operation.</summary>
internal sealed record ListImportsEntry(
    string Kind,
    string Name,
    string FilePath,
    int Line,
    bool IsGlobal,
    bool IsStatic,
    string? Alias);

/// <summary>Transport-neutral result for imported-directive analysis.</summary>
internal sealed record ListImportsResult(IReadOnlyList<ListImportsEntry> Imports);

/// <summary>
/// Shared list-imports operation. Hosts bind their own inputs and render the typed result.
/// </summary>
internal sealed class ListImportsOperation
    : CommandApplicationPort<ListImportsRequest, ListImportsResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly ImportDirectiveService _importDirective;

    public ListImportsOperation(
        IWorkspaceSessionFactory sessionFactory,
        ImportDirectiveService importDirective)
    {
        _sessionFactory = sessionFactory;
        _importDirective = importDirective;
    }

    public override ApplicationCommandDefinition Definition => new(
        ListImportsCommandDefinition.Info,
        typeof(ListImportsRequest),
        typeof(ListImportsResult),
        true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        []);

    public override async Task<Result<ListImportsResult, RtError>> ExecuteAsync(
        ListImportsRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var imports = await _importDirective.AnalyzeAsync(solution, request.ImportKind);
            return Result<ListImportsResult, RtError>.Ok(new ListImportsResult(imports.Select(importDirective => new ListImportsEntry(
                importDirective.Kind,
                importDirective.Name,
                importDirective.FilePath,
                importDirective.Line,
                importDirective.IsGlobal,
                importDirective.IsStatic,
                importDirective.Alias)).ToList()));
        }
    }
}
