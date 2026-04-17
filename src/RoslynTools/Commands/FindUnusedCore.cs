using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynTools.Commands;

internal record UnusedSymbol(string Name, string Kind, string? FilePath, int Line);

internal static class FindUnusedCore
{
    internal static async Task<List<UnusedSymbol>> AnalyzeAsync(Solution solution, string scope = "all")
    {
        var results = new List<UnusedSymbol>();

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                var semanticModel = await document.GetSemanticModelAsync();
                if (root == null || semanticModel == null)
                    continue;

                // Check named types
                if (scope is "all" or "types")
                {
                    foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(typeDecl);
                        if (symbol == null) continue;
                        if (ShouldExclude(symbol)) continue;

                        var refs = await SymbolFinder.FindReferencesAsync(symbol, solution);
                        var count = refs.Sum(r => r.Locations.Count());
                        if (count == 0)
                        {
                            var line = typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                            results.Add(new UnusedSymbol(symbol.Name, "type", document.FilePath, line));
                        }
                    }
                }

                // Check methods
                if (scope is "all" or "methods")
                {
                    foreach (var methodDecl in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
                        if (symbol == null) continue;
                        if (ShouldExclude(symbol)) continue;

                        var refs = await SymbolFinder.FindReferencesAsync(symbol, solution);
                        var count = refs.Sum(r => r.Locations.Count());
                        if (count == 0)
                        {
                            var line = methodDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                            results.Add(new UnusedSymbol(symbol.Name, "method", document.FilePath, line));
                        }
                    }
                }
            }
        }

        return results;
    }

    private static bool ShouldExclude(ISymbol symbol)
    {
        // Exclude types with serialization attributes
        if (symbol is INamedTypeSymbol type)
        {
            var attrNames = type.GetAttributes().Select(a => a.AttributeClass?.Name ?? "");
            if (attrNames.Any(n => n is "SerializableAttribute" or "Serializable"
                                    or "DataContractAttribute" or "JsonObjectAttribute"))
                return true;
        }

        // Exclude interface members
        if (symbol.ContainingType?.TypeKind == TypeKind.Interface)
            return true;

        // Exclude Main / entry points
        if (symbol.Name == "Main")
            return true;

        // Exclude xUnit test methods ([Fact], [Theory])
        if (symbol is IMethodSymbol m && m.GetAttributes().Any(a =>
                a.AttributeClass?.Name is "FactAttribute" or "TheoryAttribute"))
            return true;

        return false;
    }
}
