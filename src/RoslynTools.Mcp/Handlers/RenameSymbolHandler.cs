using RoslynTools.Mcp.Schemas;

namespace RoslynTools.Mcp.Handlers;

internal sealed class RenameSymbolHandler
{
    public async Task<RenameSymbolResult> HandleAsync(
        RenameSymbolRequest request,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement rename using RenameService
        await Task.Delay(0, cancellationToken);
        return new RenameSymbolResult(
            ChangedFiles: 0,
            Diff: null,
            Applied: false);
    }
}

internal sealed record RenameSymbolResult(int ChangedFiles, string? Diff, bool Applied);
