using RoslynTools.Mcp.Schemas;

namespace RoslynTools.Mcp.Handlers;

internal sealed class FindReferencesHandler
{
    public async Task<FindReferencesResult> HandleAsync(
        FindReferencesRequest request,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement find references using FindRefsService
        await Task.Delay(0, cancellationToken);
        return new FindReferencesResult(Symbol: request.Symbol, ReferencesCount: 0);
    }
}

internal sealed record FindReferencesResult(string Symbol, int ReferencesCount);
