using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

/// <summary>
/// Shared helpers for formatting Roslyn symbols into JSON-serializable dictionaries.
/// Extracted from InspectCommand, OutlineCommand, FindImplementationsCommand to eliminate duplication.
/// </summary>
internal static class SymbolFormatter
{
    internal static readonly JsonSerializerOptions DefaultJsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Returns a list of source-location dictionaries (file + line) for the given symbol.
    /// </summary>
    internal static List<Dictionary<string, object?>> ExtractSourceLocations(ISymbol symbol)
    {
        throw new NotImplementedException("SymbolFormatter.ExtractSourceLocations is not yet implemented.");
    }

    /// <summary>
    /// Returns a list of member dictionaries for the given named type.
    /// When <paramref name="includeStaticFlag"/> is true each entry includes an "isStatic" key.
    /// </summary>
    internal static List<Dictionary<string, object?>> ExtractMembers(
        INamedTypeSymbol type,
        bool includeStaticFlag = true)
    {
        throw new NotImplementedException("SymbolFormatter.ExtractMembers is not yet implemented.");
    }

    /// <summary>
    /// Populates <paramref name="result"/> with "baseType" and "interfaces" keys from <paramref name="type"/>.
    /// "baseType" is omitted when the base is System.Object.
    /// "interfaces" is omitted when the list is empty.
    /// </summary>
    internal static void AddTypeHierarchyInfo(
        Dictionary<string, object?> result,
        INamedTypeSymbol type)
    {
        throw new NotImplementedException("SymbolFormatter.AddTypeHierarchyInfo is not yet implemented.");
    }
}
