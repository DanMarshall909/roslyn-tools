using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class FindImplementationsService : IRtService
{
    internal async Task<List<Dictionary<string, object?>>> FindAsync(
        Solution solution,
        INamedTypeSymbol typeSymbol)
    {
        var results = new List<Dictionary<string, object?>>();
        var seen = new HashSet<string>();

        var implementations = await SymbolFinder.FindImplementationsAsync(typeSymbol, solution);

        foreach (var impl in implementations)
        {
            if (impl is not INamedTypeSymbol implType) continue;

            var fqn = implType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            if (!seen.Add(fqn)) continue;

            var locations = SymbolFormatter.ExtractSourceLocations(implType);

            var entry = new Dictionary<string, object?>
            {
                ["name"] = implType.Name,
                ["fqn"] = fqn,
                ["typeKind"] = implType.TypeKind.ToString(),
                ["accessibility"] = implType.DeclaredAccessibility.ToString(),
                ["isAbstract"] = implType.IsAbstract,
                ["project"] = implType.ContainingAssembly?.Name,
                ["locations"] = locations
            };

            var directInterfaces = implType.Interfaces
                .Select(i => i.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                .ToList();
            if (directInterfaces.Count > 0)
                entry["directInterfaces"] = directInterfaces;

            if (implType.BaseType != null && implType.BaseType.SpecialType != SpecialType.System_Object)
                entry["baseType"] = implType.BaseType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

            results.Add(entry);
        }

        if (typeSymbol.TypeKind == TypeKind.Class && typeSymbol.IsAbstract)
        {
            var derived = await SymbolFinder.FindDerivedClassesAsync(typeSymbol, solution);
            foreach (var d in derived)
            {
                var fqn = d.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                if (!seen.Add(fqn)) continue;

                var locations = SymbolFormatter.ExtractSourceLocations(d);

                results.Add(new Dictionary<string, object?>
                {
                    ["name"] = d.Name,
                    ["fqn"] = fqn,
                    ["typeKind"] = d.TypeKind.ToString(),
                    ["accessibility"] = d.DeclaredAccessibility.ToString(),
                    ["isAbstract"] = d.IsAbstract,
                    ["project"] = d.ContainingAssembly?.Name,
                    ["locations"] = locations
                });
            }
        }

        return results;
    }
}
