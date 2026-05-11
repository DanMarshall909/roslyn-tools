using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Detects unused symbols in the codebase.
/// </summary>
internal record UnusedSymbol(string Name, string Kind, string? FilePath, int Line);

/// <summary>
/// Searches for unused services in a solution.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class FindUnusedService : IRtService
{
    /// <summary>
    /// Analyzes a solution to find unused symbols.
    /// </summary>
    internal async Task<List<UnusedSymbol>> AnalyzeAsync(Solution solution, string scope = "all")
    {
        var results = new List<UnusedSymbol>();
        var rtIgnore = RtIgnore.ForSolution(solution);

        foreach (var project in solution.Projects)
        foreach (var document in project.Documents)
        {
            if (rtIgnore.IsIgnored(document.FilePath))
                continue;

            var root = await document.GetSyntaxRootAsync();
            var semanticModel = await document.GetSemanticModelAsync();
            if (root == null || semanticModel == null)
                continue;

            if (scope is "all" or "types")
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

            if (scope is "all" or "methods")
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

        return results;
    }

    private bool ShouldExclude(ISymbol symbol)
    {
        if (symbol is INamedTypeSymbol type)
        {
            var attrNames = type.GetAttributes().Select(a => a.AttributeClass?.Name ?? "");
            if (attrNames.Any(n => n is "SerializableAttribute" or "Serializable"
                    or "DataContractAttribute" or "JsonObjectAttribute"))
                return true;
        }

        if (symbol.ContainingType?.TypeKind == TypeKind.Interface)
            return true;

        if (symbol.Name == "Main")
            return true;

        if (symbol is IMethodSymbol m && m.GetAttributes().Any(a =>
                a.AttributeClass?.Name is "FactAttribute" or "TheoryAttribute"))
            return true;

        return false;
    }
}
