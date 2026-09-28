using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// An interface for symbol resolution services in the Roslyn tooling ecosystem
/// </summary>
internal interface ISymbolResolutionService
{
    Task<Result<ISymbol, RtError>> TryFindSymbolAsync(Solution solution, string symbolName, string? kind);

    Task<Result<ISymbol, RtError>> TryFindSymbolByFqnAsync(Solution solution, string fullyQualifiedName, string? kind);

    Task<Result<ISymbol, RtError>> TryFindMemberBySignatureAsync(
        Solution solution, string signature, bool includeSuggestions = true);

    Task<ISymbol> FindSymbolAsync(Solution solution, string symbolName, string? kind);

    Task<ISymbol> FindSymbolByFqnAsync(Solution solution, string fullyQualifiedName, string? kind);

    Task<ISymbol> FindTypeByFqnAsync(Solution solution, string fqn);

    Task<IMethodSymbol> FindMethodBySpecAsync(Solution solution, string methodSpec);

    Task<IMethodSymbol?> FindMethodInTypeAsync(Solution solution, string typeName, string methodName);
}

/// <summary>
/// A class for symbol resolution in the Roslyn tooling ecosystem
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance service registered through DI for dependency inversion.")]
internal sealed class SymbolResolutionService : ISymbolResolutionService, IRtService
{
    public Task<Result<ISymbol, RtError>> TryFindSymbolAsync(Solution solution, string symbolName, string? kind)
    {
        return SymbolLocator.TryFindSymbolAsync(solution, symbolName, kind);
    }

    public Task<Result<ISymbol, RtError>> TryFindSymbolByFqnAsync(Solution solution, string fullyQualifiedName, string? kind)
    {
        return SymbolLocator.TryFindSymbolByFqnAsync(solution, fullyQualifiedName, kind);
    }

    public Task<Result<ISymbol, RtError>> TryFindMemberBySignatureAsync(
        Solution solution, string signature, bool includeSuggestions = true)
    {
        return SymbolLocator.TryFindMemberBySignatureAsync(
            solution, signature, includeSuggestions);
    }

    public Task<ISymbol> FindSymbolAsync(Solution solution, string symbolName, string? kind)
    {
        return SymbolLocator.FindSymbolAsync(solution, symbolName, kind);
    }

    public Task<ISymbol> FindSymbolByFqnAsync(Solution solution, string fullyQualifiedName, string? kind)
    {
        return SymbolLocator.FindSymbolByFqnAsync(solution, fullyQualifiedName, kind);
    }

    public Task<ISymbol> FindTypeByFqnAsync(Solution solution, string fqn)
    {
        return SymbolLocator.FindTypeByFqnAsync(solution, fqn);
    }

    public Task<IMethodSymbol> FindMethodBySpecAsync(Solution solution, string methodSpec)
    {
        return SymbolLocator.FindMethodBySpecAsync(solution, methodSpec);
    }

    public Task<IMethodSymbol?> FindMethodInTypeAsync(Solution solution, string typeName, string methodName)
    {
        return SymbolLocator.FindMethodInTypeAsync(solution, typeName, methodName);
    }
}
