using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Services.Suggest;

namespace RoslynTools.Services;

internal sealed record DescribeRequest(
    [property: JsonRequired] string Symbol,
    string? Solution = null,
    string? Kind = null,
    bool WithLocations = false);

internal enum DescribeResultKind
{
    Compact,
    Verbose
}

/// <summary>Explicit compact-or-verbose result returned by the shared describe operation.</summary>
internal sealed record DescribeResult(
    DescribeResultKind Kind,
    CompactDescription? Compact,
    JsonElement? Verbose);

/// <summary>Shared describe operation used by CLI and descriptor-driven hosts.</summary>
internal sealed class DescribeOperation(
    IWorkspaceSessionFactory sessionFactory,
    InspectService inspect) : CommandApplicationPort<DescribeRequest, DescribeResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;
    private readonly InspectService _inspect = inspect;

    public override ApplicationCommandDefinition Definition => DescribeApplicationDefinition.Definition;

    public override async Task<Result<DescribeResult, RtError>> ExecuteAsync(
        DescribeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inspection = await _inspect.Describe(
                solution,
                request.Symbol,
                request.Kind,
                request.WithLocations);

            return inspection.Map(result => result is CompactDescription compact
                ? new DescribeResult(
                    DescribeResultKind.Compact,
                    compact with
                    {
                        Suggestions = SuggestionHelper.ForDescribe(compact, request.Solution)
                    },
                    null)
                : new DescribeResult(
                    DescribeResultKind.Verbose,
                    null,
                    JsonSerializer.SerializeToElement(result, SymbolFormatter.DefaultJsonOptions)));
        }
    }
}
