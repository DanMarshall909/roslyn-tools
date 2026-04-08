using Microsoft.CodeAnalysis;

namespace RoslynTools.Models;

internal sealed class SymbolMatch
{
    public required ISymbol Symbol { get; init; }
    public required string DisplayName { get; init; }
    public required string ProjectName { get; init; }
}
