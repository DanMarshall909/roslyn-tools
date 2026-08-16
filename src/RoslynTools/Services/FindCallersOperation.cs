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
    : CommandApplicationPort<FindCallersRequest, Result<FindCallersResult, RtError>>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly FindCallersService _findCallers;

    public FindCallersOperation(
        IWorkspaceSessionFactory sessionFactory,
        FindCallersService findCallers)
    {
        _sessionFactory = sessionFactory;
        _findCallers = findCallers;
    }

    public override string CommandName => "find_callers";

    public override async Task<Result<FindCallersResult, RtError>> ExecuteAsync(
        FindCallersRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var solution = await _sessionFactory.GetSolutionAsync(request.Solution);
        cancellationToken.ThrowIfCancellationRequested();
        return await _findCallers.FindAsync(solution, request.Symbol, request.IncludeTests);
    }

    internal bool IsNameofExpression(Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax invocation)
    {
        return _findCallers.IsNameofExpression(invocation);
    }
}
