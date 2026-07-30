using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Services.Results;
using RoslynTools.Services.Suggest;

namespace RoslynTools.Services;

internal sealed record FindRefsRequest(
    [property: JsonRequired] string Symbol,
    string? Solution = null,
    string? Kind = null);

/// <summary>Shared find-references operation used by CLI and MCP.</summary>
internal sealed class FindRefsOperation
    : CommandApplicationPort<FindRefsRequest, FindRefsResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly FindRefsService _findRefs;

    public FindRefsOperation(
        IWorkspaceSessionFactory sessionFactory,
        FindRefsService findRefs)
    {
        _sessionFactory = sessionFactory;
        _findRefs = findRefs;
    }

    public override string CommandName => "find_refs";

    public override async Task<FindRefsResult> ExecuteAsync(
        FindRefsRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var solution = await _sessionFactory.GetSolutionAsync(request.Solution);
        var result = await _findRefs.FindAsync(solution, request.Symbol, request.Kind);
        return result with
        {
            Suggestions = SuggestionHelper.ForFindRefs(
                result.FullyQualifiedName,
                request.Solution)
        };
    }
}
