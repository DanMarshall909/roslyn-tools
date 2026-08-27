using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services.Suggest;

internal sealed record SuggestOperationRequest(
    [property: JsonRequired] string Query,
    int Top = 3);

/// <summary>Shared catalog-suggestion operation used by CLI and MCP.</summary>
internal sealed class SuggestOperation
    : LegacyCommandApplicationPort<SuggestOperationRequest, SuggestResponse>, IRtService
{
    private readonly SuggestService _suggest;

    public SuggestOperation(SuggestService suggest)
    {
        _suggest = suggest;
    }

    public override string CommandName => "suggest_command";

    public override Task<SuggestResponse> ExecuteAsync(
        SuggestOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            _suggest.Suggest(new SuggestRequest(request.Query, request.Top)));
    }
}
