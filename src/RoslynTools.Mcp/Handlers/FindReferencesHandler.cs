using RoslynTools.Core;
using RoslynTools.Mcp.Schemas;
using RoslynTools.Services;

namespace RoslynTools.Mcp.Handlers;

/// <summary>
/// Handler for finding references in the Roslyn project.
/// </summary>
internal sealed class FindReferencesHandler
{
    private readonly FindRefsService _findRefs;
    private readonly IWorkspaceSessionFactory _workspaceLoader;

    public FindReferencesHandler(IWorkspaceSessionFactory workspaceLoader, FindRefsService findRefs)
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

/// <summary>
/// Result class for the FindReferencesHandler method.
/// </summary>
internal sealed record FindReferencesResult(string Symbol, int ReferencesCount);
