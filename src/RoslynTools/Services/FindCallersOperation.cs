using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Services.Results;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for finding invocation sites of a method.</summary>
internal sealed record FindCallersRequest(
    [property: JsonRequired] string Symbol,
    string? Solution = null,
    [property: JsonPropertyName("include-tests")] bool IncludeTests = false);

/// <summary>Shared find-callers operation used by CLI and protocol adapters.</summary>
internal sealed class FindCallersOperation
    : ICommandApplicationPort, IRtService
{
    private static readonly JsonSerializerOptions ErrorSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly FindCallersService _findCallers;

    public FindCallersOperation(
        IWorkspaceSessionFactory sessionFactory,
        FindCallersService findCallers)
    {
        _sessionFactory = sessionFactory;
        _findCallers = findCallers;
    }

    public string CommandName => "find_callers";
    public Type RequestType => typeof(FindCallersRequest);

    internal async Task<Result<FindCallersResult, RtError>> ExecuteAsync(
        FindCallersRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var solution = await _sessionFactory.GetSolutionAsync(request.Solution);
        cancellationToken.ThrowIfCancellationRequested();
        return await _findCallers.FindAsync(solution, request.Symbol, request.IncludeTests);
    }

    async Task<CommandPortResult> ICommandApplicationPort.InvokeAsync(
        object request,
        CancellationToken cancellationToken)
    {
        if (request is not FindCallersRequest typedRequest)
            throw new ArgumentException("Invalid find-callers request.", nameof(request));

        var result = await ExecuteAsync(typedRequest, cancellationToken);
        return result.Match(
            CommandPortResult.Success,
            error => CommandPortResult.Failure(
                JsonSerializer.Serialize(error, ErrorSerializerOptions)));
    }

    internal bool IsNameofExpression(Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax invocation)
    {
        return _findCallers.IsNameofExpression(invocation);
    }
}
