using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Services.Suggest;

namespace RoslynTools.Services;

internal sealed record DescribeRequest(
    [property: JsonRequired] string Symbol,
    string? Solution = null,
    string? Kind = null,
    bool WithLocations = false);

/// <summary>Shared describe operation used by CLI and descriptor-driven hosts.</summary>
internal sealed class DescribeOperation : ICommandApplicationPort, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly InspectService _inspect;

    public DescribeOperation(
        IWorkspaceSessionFactory sessionFactory,
        InspectService inspect)
    {
        _sessionFactory = sessionFactory;
        _inspect = inspect;
    }

    public string CommandName => "describe";
    public Type RequestType => typeof(DescribeRequest);

    internal async Task<Result<object, RtError>> ExecuteAsync(
        DescribeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var solution = await _sessionFactory.GetSolutionAsync(request.Solution);
        var inspection = await _inspect.Describe(
            solution,
            request.Symbol,
            request.Kind,
            request.WithLocations);

        return inspection.Map(result =>
        {
            if (result is CompactDescription compact)
                return (object)(compact with
                {
                    Suggestions = SuggestionHelper.ForDescribe(compact, request.Solution)
                });
            return result;
        });
    }

    async Task<CommandPortResult> ICommandApplicationPort.InvokeAsync(
        object request,
        CancellationToken cancellationToken)
    {
        if (request is not DescribeRequest typedRequest)
            throw new ArgumentException("Invalid describe request.", nameof(request));

        var result = await ExecuteAsync(typedRequest, cancellationToken);
        return result.Match(
            CommandPortResult.Success,
            error => CommandPortResult.Failure(error.Message));
    }
}
