using RoslynTools.Core;
using RoslynTools.Mcp.Schemas;
using RoslynTools.Services;

namespace RoslynTools.Mcp.Handlers;

internal sealed class RenameSymbolHandler
{
    public async Task<RenameSymbolResult> HandleAsync(
        RenameSymbolRequest request,
        CancellationToken cancellationToken = default)
    {
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var newSln = await new RenameService().RenameAsync(sln, request.OldName, request.NewName, kind: null);

            var changedFiles = newSln.GetChanges(sln)
                .GetProjectChanges()
                .SelectMany(p => p.GetChangedDocuments())
                .Count();

            if (request.Apply)
            {
                await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
                return new RenameSymbolResult(ChangedFiles: changedFiles, Applied: true);
            }

            return new RenameSymbolResult(ChangedFiles: changedFiles, Applied: false);
        }
    }
}

internal sealed record RenameSymbolResult(int ChangedFiles, bool Applied);
