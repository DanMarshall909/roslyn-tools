using RoslynTools.Core;
using RoslynTools.Mcp.Schemas;
using RoslynTools.Services;

namespace RoslynTools.Mcp.Handlers;

internal sealed class FindReferencesHandler
{
    private readonly FindRefsService _findRefs;
    private readonly WorkspaceLoader _workspaceLoader;

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
        var refs = await _findRefs.FindAsync(sln, request.Symbol, null);
        return new FindReferencesResult(refs.SymbolName, refs.References.Count);
    }
}

internal sealed record FindReferencesResult(string Symbol, int ReferencesCount);
