using RoslynTools.Core;
using RoslynTools.Mcp.Schemas;
using RoslynTools.Services;

namespace RoslynTools.Mcp.Handlers;

/// <summary>
/// Handler for renaming symbols in the Roslyn project.
/// </summary>
internal sealed class RenameSymbolHandler
{
    private readonly RenameService _rename;
    private readonly ISolutionChangeWriter _solutionChangeWriter;
    private readonly WorkspaceLoader _workspaceLoader;

    public RenameSymbolHandler(
        WorkspaceLoader workspaceLoader,
        RenameService rename,
        ISolutionChangeWriter solutionChangeWriter)
    {
        _workspaceLoader = workspaceLoader;
        _rename = rename;
        _solutionChangeWriter = solutionChangeWriter;
    }

    public async Task<RenameSymbolResult> HandleAsync(
        RenameSymbolRequest request,
        CancellationToken cancellationToken = default)
    {
        var sln = await _workspaceLoader.GetSolutionAsync(request.Solution);

        cancellationToken.ThrowIfCancellationRequested();
        var newSln = await _rename.RenameAsync(sln, request.OldName, request.NewName, null);

        var changedFiles = newSln.GetChanges(sln)
            .GetProjectChanges()
            .SelectMany(p => p.GetChangedDocuments())
            .Count();

        if (request.Apply)
        {
            await _solutionChangeWriter.ApplyChangesAsync(sln, newSln, false);
            return new RenameSymbolResult(changedFiles, true);
        }

        return new RenameSymbolResult(changedFiles, false);
    }
}

/// <summary>
/// Result class for the RenameSymbolHandler method.
/// </summary>
internal sealed record RenameSymbolResult(int ChangedFiles, bool Applied);
