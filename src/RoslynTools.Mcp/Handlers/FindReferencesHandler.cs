using RoslynTools.Core;
using RoslynTools.Mcp.Schemas;
using RoslynTools.Services;

namespace RoslynTools.Mcp.Handlers;

internal sealed class FindReferencesHandler
{
    public async Task<FindReferencesResult> HandleAsync(
        FindReferencesRequest request,
        CancellationToken cancellationToken = default)
    {
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var refs = await new FindRefsService().FindAsync(sln, request.Symbol, kind: null);
            return new FindReferencesResult(Symbol: refs.SymbolName, ReferencesCount: refs.References.Count);
        }
    }
}

internal sealed record FindReferencesResult(string Symbol, int ReferencesCount);
