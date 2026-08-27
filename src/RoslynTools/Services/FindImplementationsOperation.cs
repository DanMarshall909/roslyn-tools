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
    IReadOnlyList<Dictionary<string, object?>> Implementations,
    [property: JsonIgnore] bool IsAbstract)
{
    public int ImplementationCount => Implementations.Count;
}

/// <summary>Shared find-implementations operation used by host adapters.</summary>
internal sealed class FindImplementationsOperation
    : LegacyCommandApplicationPort<FindImplementationsRequest, FindImplementationsResult>, IRtService
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

    public override string CommandName => "find_impl";

    public override async Task<FindImplementationsResult> ExecuteAsync(
        FindImplementationsRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var solution = await _sessionFactory.GetSolutionAsync(request.Solution);
        cancellationToken.ThrowIfCancellationRequested();

        var symbol = request.Symbol.Contains('.', StringComparison.Ordinal)
            ? await _symbols.FindSymbolByFqnAsync(solution, request.Symbol, "type")
            : await _symbols.FindSymbolAsync(solution, request.Symbol, "type");

        if (symbol is not INamedTypeSymbol typeSymbol)
            throw new InvalidOperationException($"'{request.Symbol}' is not a type (found {symbol.Kind}).");

        var implementations = await _findImplementations.FindAsync(solution, typeSymbol);
        return new FindImplementationsResult(
            typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            typeSymbol.TypeKind.ToString(),
            implementations,
            typeSymbol.IsAbstract);
    }
}
