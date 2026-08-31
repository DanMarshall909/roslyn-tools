using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for finding invocation sites of a method.</summary>
internal sealed record FindCallersRequest(
    [property: JsonRequired] string Symbol,
    string? Solution = null,
    [property: JsonPropertyName("include-tests")] bool IncludeTests = false);

internal sealed record CallerOperationResult(
    string FilePath,
    int Line,
    string ContainingMethod,
    string SourceLine);

internal sealed record FindCallersOperationResult(
    string SymbolName,
    IReadOnlyList<CallerOperationResult> Callers);

/// <summary>Shared find-callers operation used by CLI and protocol adapters.</summary>
internal sealed class FindCallersOperation(
    IWorkspaceSessionFactory sessionFactory,
    FindCallersService findCallers)
    : CommandApplicationPort<FindCallersRequest, FindCallersOperationResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;
    private readonly FindCallersService _findCallers = findCallers;

    public override ApplicationCommandDefinition Definition => FindCallersCommandDefinition.Definition;

    public override async Task<Result<FindCallersOperationResult, RtError>> ExecuteAsync(
        FindCallersRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _findCallers.FindAsync(
                solution,
                request.Symbol,
                request.IncludeTests,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result.Map(value => new FindCallersOperationResult(
                value.SymbolName,
                value.Callers.Select(static caller => new CallerOperationResult(
                    caller.FilePath,
                    caller.Line,
                    caller.ContainingMethod,
                    caller.SourceLine)).ToList()));
        }
    }
}
