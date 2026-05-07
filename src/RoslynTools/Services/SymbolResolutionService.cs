using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal interface ISymbolResolutionService
{
    Task<ISymbol> FindSymbolAsync(Solution solution, string symbolName, string? kind);

    Task<ISymbol> FindSymbolByFqnAsync(Solution solution, string fullyQualifiedName, string? kind);

    Task<ISymbol> FindTypeByFqnAsync(Solution solution, string fqn);

    Task<IMethodSymbol> FindMethodBySpecAsync(Solution solution, string methodSpec);

    Task<IMethodSymbol?> FindMethodInTypeAsync(Solution solution, string typeName, string methodName);
}

[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance service registered through DI for dependency inversion.")]
internal sealed class SymbolResolutionService : ISymbolResolutionService, IRtService
{
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
