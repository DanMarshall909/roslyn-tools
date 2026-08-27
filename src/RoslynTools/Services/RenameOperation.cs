using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Models;

namespace RoslynTools.Services;

internal sealed record RenameOperationRequest(
    [property: JsonRequired] string Symbol,
    [property: JsonRequired, JsonPropertyName("new-name")] string NewName,
    string? Kind = null,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = true,
    [property: JsonPropertyName("related-edits")] string[]? RelatedEdits = null);

/// <summary>Shared semantic rename operation used by CLI and MCP.</summary>
internal sealed class RenameOperation
    : LegacyCommandApplicationPort<RenameOperationRequest, RenameResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly RenameService _rename;

    public RenameOperation(
        IWorkspaceSessionFactory sessionFactory,
        RenameService rename)
    {
        _sessionFactory = sessionFactory;
        _rename = rename;
    }

    public override string CommandName => "rename";

    public override async Task<RenameResult> ExecuteAsync(
        RenameOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) =
            await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            return await _rename.ExecuteAsync(
                solution,
                new RenameRequest(
                    request.Symbol,
                    request.NewName,
                    request.Kind,
                    request.DryRun,
                    (request.RelatedEdits ?? []).ToHashSet(StringComparer.Ordinal)));
        }
    }
}
