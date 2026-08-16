using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for retrieving a symbol's source declaration.</summary>
internal sealed record SourceRequest(
    [property: JsonRequired] string Symbol,
    string? Solution = null,
    string? Kind = null);

/// <summary>
/// Shared source operation. Hosts bind their own inputs and render the legacy source-entry shape.
/// </summary>
internal sealed class SourceOperation
    : CommandApplicationPort<SourceRequest, List<Dictionary<string, object?>>>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly InspectService _inspect;

    public SourceOperation(
        IWorkspaceSessionFactory sessionFactory,
        InspectService inspect)
    {
        _sessionFactory = sessionFactory;
        _inspect = inspect;
    }

    public override string CommandName => "source";

    public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
        SourceRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var solution = await _sessionFactory.GetSolutionAsync(request.Solution);
        cancellationToken.ThrowIfCancellationRequested();
        var symbol = await SymbolLocator.FindSymbolByFqnAsync(solution, request.Symbol, request.Kind);
        cancellationToken.ThrowIfCancellationRequested();
        return await _inspect.GetSourceAsync(symbol);
    }
}
