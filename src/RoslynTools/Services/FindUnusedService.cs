using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// How far a finding can be trusted. find-unused emits a safe-delete suggestion only at
/// <see cref="High"/>, so anything the analysis cannot see through must be reported at
/// <see cref="Low"/> rather than excluded outright — the finding stays visible, but it
/// cannot invite a deletion.
/// </summary>
internal static class UnusedConfidence
{
    internal const string High = "high";
    internal const string Low = "low";
}

/// <summary>
/// Standard <see cref="UnusedSymbol.Reason"/> values.
/// </summary>
internal static class UnusedReason
{
    internal const string NoSourceReferences = "No source references were found.";
}

/// <summary>
/// Detects unused symbols in the codebase.
/// </summary>
internal record UnusedSymbol(
    string Name,
    string Kind,
    string? FilePath,
    int Line,
    string Confidence = UnusedConfidence.High,
    string Reason = UnusedReason.NoSourceReferences);

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
        var reflectivelyScannedProjects = await FindReflectivelyScannedProjectsAsync(solution);

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
                        if (semanticModel.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol symbol)
                            continue;

                        if (ShouldExclude(symbol)) continue;

                        var refs = await SymbolFinder.FindReferencesAsync(symbol, solution);
                        var count = refs.Sum(r => r.Locations.Count());
                        if (count != 0)
                            continue;

                        // Assembly scanning intentionally trades false negatives for zero false positives.
                        // find-unused can trigger deletion, so a live type must never be labelled dead merely
                        // because its construction happens through a reflective container.
                        if (reflectivelyScannedProjects.Contains(project.Id) && IsInstantiable(symbol))
                            continue;

                        var line = typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        results.Add(new UnusedSymbol(symbol.Name, "type", document.FilePath, line));
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

    private static async Task<HashSet<ProjectId>> FindReflectivelyScannedProjectsAsync(Solution solution)
    {
        var projects = new HashSet<ProjectId>();
        foreach (var project in solution.Projects)
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                if (root == null)
                    continue;

                var hasAssemblyScan = root.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Select(i => i.Expression.ToString())
                    .Any(e => e.Contains("FromAssemblyOf", StringComparison.Ordinal)
                              || e.Contains("FromAssemblies", StringComparison.Ordinal)
                              || e.EndsWith(".AddClasses", StringComparison.Ordinal)
                              || e == "AddClasses");

                if (hasAssemblyScan)
                {
                    projects.Add(project.Id);
                    break;
                }
            }

        return projects;
    }

    private static bool IsInstantiable(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Class
        && !type.IsAbstract
        && !type.IsStatic;

    private bool ShouldExclude(ISymbol symbol)
    {
        if (symbol is INamedTypeSymbol type)
        {
            if (HasAttribute(type, "SerializableAttribute", "Serializable", "DataContractAttribute",
                    "JsonObjectAttribute", "ApiControllerAttribute", "UsedImplicitlyAttribute"))
                return true;

            if (InheritsFrom(type, "ControllerBase"))
                return true;

            if (IsXunitFixture(type))
                return true;
        }

        if (symbol.ContainingType?.TypeKind == TypeKind.Interface)
            return true;

        if (symbol.Name == "Main")
            return true;

        if (symbol is IMethodSymbol m && HasAttribute(m, "FactAttribute", "TheoryAttribute"))
            return true;

        return false;
    }

    private static bool IsXunitFixture(INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.BaseType)
            if (current.GetMembers().OfType<IMethodSymbol>()
                .Any(m => HasAttribute(m, "FactAttribute", "TheoryAttribute")))
                return true;

        return false;
    }

    private static bool InheritsFrom(INamedTypeSymbol type, string baseTypeName)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
            if (current.Name == baseTypeName)
                return true;

        return false;
    }

    private static bool HasAttribute(ISymbol symbol, params string[] names)
    {
        var expected = names.ToHashSet(StringComparer.Ordinal);
        return symbol.GetAttributes().Any(a =>
            a.AttributeClass is { } attribute
            && (expected.Contains(attribute.Name) || expected.Contains(attribute.MetadataName)));
    }
}
