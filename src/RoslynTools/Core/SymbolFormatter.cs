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
        return symbol.Locations
            .Where(l => l.IsInSource)
            .Select(l =>
            {
                var span = l.GetLineSpan();
                return new Dictionary<string, object?>
                {
                    ["file"] = span.Path,
                    ["line"] = span.StartLinePosition.Line + 1,
                };
            })
            .ToList();
    }

    /// <summary>
    /// Returns a list of member dictionaries for the given named type.
    /// When <paramref name="includeStaticFlag"/> is true each entry includes an "isStatic" key.
    /// </summary>
    internal static List<Dictionary<string, object?>> ExtractMembers(
        INamedTypeSymbol type,
        bool includeStaticFlag = true)
    {
        return type.GetMembers()
            .Where(m => !m.IsImplicitlyDeclared)
            .Select(m =>
            {
                var entry = new Dictionary<string, object?>
                {
                    ["name"] = m.Name,
                    ["kind"] = m.Kind.ToString(),
                    ["signature"] = m.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    ["accessibility"] = m.DeclaredAccessibility.ToString(),
                };
                if (includeStaticFlag)
                    entry["isStatic"] = m.IsStatic;
                return entry;
            })
            .ToList();
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
        if (type.BaseType != null && type.BaseType.SpecialType != SpecialType.System_Object)
            result["baseType"] = type.BaseType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        var interfaces = type.Interfaces
            .Select(i => i.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
            .ToList();
        if (interfaces.Count > 0)
            result["interfaces"] = interfaces;
    }
}
