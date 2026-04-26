using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal static class OutlineService
{
    internal static async Task<List<Dictionary<string, object?>>> OutlineFileAsync(
        Solution solution,
        string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var results = new List<Dictionary<string, object?>>();

        foreach (var project in solution.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (!string.Equals(doc.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                var semanticModel = await doc.GetSemanticModelAsync();
                if (semanticModel == null) continue;

                var root = await doc.GetSyntaxRootAsync();
                if (root == null) continue;

                var declaredSymbols = root.DescendantNodes()
                    .Select(n => semanticModel.GetDeclaredSymbol(n))
                    .Where(s => s is INamedTypeSymbol)
                    .Cast<INamedTypeSymbol>()
                    .Distinct(SymbolEqualityComparer.Default)
                    .Cast<INamedTypeSymbol>();

                foreach (var type in declaredSymbols)
                    results.Add(BuildTypeOutline(type));
            }
        }

        if (results.Count == 0)
            throw new InvalidOperationException($"No types found in file: {fullPath}");

        return results;
    }

    internal static async Task<List<Dictionary<string, object?>>> OutlineNamespaceAsync(
        Solution solution,
        string namespaceName)
    {
        var results = new List<Dictionary<string, object?>>();
        var seen = new HashSet<string>();

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            var nsSymbols = FindNamespaceTypes(compilation.GlobalNamespace, namespaceName);
            foreach (var type in nsSymbols)
            {
                var key = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                if (seen.Add(key))
                    results.Add(BuildTypeOutline(type));
            }
        }

        if (results.Count == 0)
            throw new InvalidOperationException($"No types found in namespace: {namespaceName}");

        return results;
    }

    internal static IEnumerable<INamedTypeSymbol> FindNamespaceTypes(
        INamespaceSymbol ns,
        string targetName)
    {
        if (ns.ToDisplayString() == targetName)
        {
            foreach (var type in ns.GetTypeMembers())
                yield return type;
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in FindNamespaceTypes(child, targetName))
                yield return type;
        }
    }

    internal static Dictionary<string, object?> BuildTypeOutline(INamedTypeSymbol type)
    {
        var outline = new Dictionary<string, object?>
        {
            ["name"] = type.Name,
            ["fqn"] = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            ["kind"] = type.TypeKind.ToString(),
            ["accessibility"] = type.DeclaredAccessibility.ToString(),
        };

        SymbolFormatter.AddTypeHierarchyInfo(outline, type);

        outline["members"] = SymbolFormatter.ExtractMembers(type, includeStaticFlag: false);

        return outline;
    }
}
