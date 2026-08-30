using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for finding implementations of a type.</summary>
internal sealed record FindImplementationsRequest(
    [property: JsonRequired] string Symbol,
    string? Solution = null);

/// <summary>Structured result for a find-implementations request.</summary>
internal sealed record FindImplementationsResult(
    string Symbol,
    string SymbolKind,
    IReadOnlyList<ImplementationItem> Implementations,
    [property: JsonIgnore] bool IsAbstract)
{
    public int ImplementationCount => Implementations.Count;
}

/// <summary>Shared find-implementations operation used by host adapters.</summary>
internal sealed class FindImplementationsOperation
    : CommandApplicationPort<FindImplementationsRequest, FindImplementationsResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly FindImplementationsService _findImplementations;
    private readonly ISymbolResolutionService _symbols;

    public FindImplementationsOperation(
        IWorkspaceSessionFactory sessionFactory,
        FindImplementationsService findImplementations,
        ISymbolResolutionService symbols)
    {
        _sessionFactory = sessionFactory;
        _findImplementations = findImplementations;
        _symbols = symbols;
    }

    public override ApplicationCommandDefinition Definition => FindImplementationsCommandDefinition.Definition;

    public override async Task<Result<FindImplementationsResult, RtError>> ExecuteAsync(
        FindImplementationsRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolved = request.Symbol.Contains('.', StringComparison.Ordinal)
                ? await _symbols.TryFindSymbolByFqnAsync(solution, request.Symbol, "type")
                : await _symbols.TryFindSymbolAsync(solution, request.Symbol, "type");
            if (resolved.IsErr)
                return resolved.Map(_ => default(FindImplementationsResult)!);

            var symbol = resolved.Match(value => value, _ => default!);
            if (symbol is not INamedTypeSymbol typeSymbol)
            {
                return Result<FindImplementationsResult, RtError>.Err(new RtError(
                    RtErrorKind.InvalidInput,
                    $"'{request.Symbol}' is not a type (found {symbol.Kind})."));
            }

            var implementations = await _findImplementations.FindAsync(solution, typeSymbol);
            cancellationToken.ThrowIfCancellationRequested();
            return Result<FindImplementationsResult, RtError>.Ok(new FindImplementationsResult(
                typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                typeSymbol.TypeKind.ToString(),
                implementations,
                typeSymbol.IsAbstract));
        }
    }
}
