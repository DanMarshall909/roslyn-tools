using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record ImplementationLocation(string File, int Line);

internal sealed record ImplementationItem(
    string Name,
    string Fqn,
    string TypeKind,
    string Accessibility,
    bool IsAbstract,
    string? Project,
    IReadOnlyList<ImplementationLocation> Locations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? DirectInterfaces,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BaseType);

/// <summary>
/// Provides methods for finding implementations of a type symbol.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class FindImplementationsService : IRtService
{
    /// <summary>
    /// Async method to find implementations based on a type symbol.
    /// </summary>
    internal async Task<List<ImplementationItem>> FindAsync(
        Solution solution,
        INamedTypeSymbol typeSymbol)
    {
        var results = new List<ImplementationItem>();
        var seen = new HashSet<string>();

        var implementations = await SymbolFinder.FindImplementationsAsync(typeSymbol, solution);

        foreach (var impl in implementations)
        {
            if (impl is not INamedTypeSymbol implType) continue;

            var fqn = implType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            if (!seen.Add(fqn)) continue;

            var locations = SymbolFormatter.ExtractSourceLocations(implType);

            var locationItems = locations.Select(static location => new ImplementationLocation(
                (string)location["file"]!,
                (int)location["line"]!)).ToList();

            var directInterfaces = implType.Interfaces
                .Select(i => i.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                .ToList();
            results.Add(new ImplementationItem(
                implType.Name,
                fqn,
                implType.TypeKind.ToString(),
                implType.DeclaredAccessibility.ToString(),
                implType.IsAbstract,
                implType.ContainingAssembly?.Name,
                locationItems,
                directInterfaces.Count > 0 ? directInterfaces : null,
                implType.BaseType is { SpecialType: not SpecialType.System_Object } baseType
                    ? baseType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                    : null));
        }

        if (typeSymbol.TypeKind == TypeKind.Class && typeSymbol.IsAbstract)
        {
            var derived = await SymbolFinder.FindDerivedClassesAsync(typeSymbol, solution);
            foreach (var d in derived)
            {
                var fqn = d.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                if (!seen.Add(fqn)) continue;

                var locations = SymbolFormatter.ExtractSourceLocations(d);

                results.Add(new ImplementationItem(
                    d.Name,
                    fqn,
                    d.TypeKind.ToString(),
                    d.DeclaredAccessibility.ToString(),
                    d.IsAbstract,
                    d.ContainingAssembly?.Name,
                    locations.Select(static location => new ImplementationLocation(
                        (string)location["file"]!,
                        (int)location["line"]!)).ToList(),
                    null,
                    null));
            }
        }

        return results;
    }
}
