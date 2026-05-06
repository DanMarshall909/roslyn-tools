using RoslynTools.Core;
using RoslynTools.Mcp.Schemas;
using RoslynTools.Services;

namespace RoslynTools.Mcp.Handlers;

internal sealed class FindReferencesHandler
{
    private readonly WorkspaceLoader _workspaceLoader;
    private readonly FindRefsService _findRefs;

    public FindReferencesHandler(WorkspaceLoader workspaceLoader, FindRefsService findRefs)
    {
        _workspaceLoader = workspaceLoader;
        _findRefs = findRefs;
    }

    public async Task<FindReferencesResult> HandleAsync(
        FindReferencesRequest request,
        CancellationToken cancellationToken = default)
    {
        var sln = await _workspaceLoader.GetSolutionAsync(request.Solution);

        cancellationToken.ThrowIfCancellationRequested();
        var refs = await _findRefs.FindAsync(sln, request.Symbol, kind: null);
        return new FindReferencesResult(Symbol: refs.SymbolName, ReferencesCount: refs.References.Count);
    }
}

internal sealed record FindReferencesResult(string Symbol, int ReferencesCount);
