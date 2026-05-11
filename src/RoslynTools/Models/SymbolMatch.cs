using Microsoft.CodeAnalysis;

namespace RoslynTools.Models;

/// <summary>
/// A class for handling symbol matching operations.
/// </summary>
internal sealed class SymbolMatch
{
    public required ISymbol Symbol { get; init; }
    public required string DisplayName { get; init; }
    public required string ProjectName { get; init; }
}
