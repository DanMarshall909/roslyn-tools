using Microsoft.CodeAnalysis;
using System.Diagnostics.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance class created by rt destatic-class migration.")]
internal class OutlineService : IRtService
{
    internal async Task<List<Dictionary<string, object?>>> OutlineFileAsync(
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

    internal async Task<List<Dictionary<string, object?>>> OutlineNamespaceAsync(
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

    internal IEnumerable<INamedTypeSymbol> FindNamespaceTypes(
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

    internal Dictionary<string, object?> BuildTypeOutline(INamedTypeSymbol type)
    {
        var (lineFrom, lineTo) = SymbolFormatter.GetSourceLineRange(type);
        var outline = new Dictionary<string, object?>
        {
            ["name"] = type.Name,
            ["fqn"] = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            ["kind"] = type.TypeKind.ToString(),
            ["accessibility"] = type.DeclaredAccessibility.ToString(),
            ["lineFrom"] = lineFrom,
            ["lineTo"] = lineTo,
        };

        SymbolFormatter.AddTypeHierarchyInfo(outline, type);

        // Group members by kind, then accessibility, to save tokens (kind/accessibility appear
        // once per group instead of on every member). Each member entry strips those redundant
        // keys but keeps name + signature + lineFrom/lineTo.
        outline["members"] = GroupMembersByKindAndAccessibility(
            SymbolFormatter.ExtractMembers(type, includeStaticFlag: false));

        return outline;
    }

    // C# convention order — Field > Property > Event > Method > NamedType (nested types last).
    // Anything not in this list (rare — e.g. ErrorType) sorts alphabetically at the end.
    private readonly string[] KindOrder = ["Field", "Property", "Event", "Method", "NamedType"];

    // Public-first; private last. Mirrors how a reader scans an unfamiliar type.
    private readonly string[] AccessibilityOrder =
        ["Public", "Internal", "ProtectedOrInternal", "Protected", "ProtectedAndInternal", "Private", "NotApplicable"];

    private Dictionary<string, object?> GroupMembersByKindAndAccessibility(
        List<Dictionary<string, object?>> members)
    {
        var grouped = new Dictionary<string, object?>(StringComparer.Ordinal);

        var byKind = members
            .GroupBy(m => (string)m["kind"]!)
            .OrderBy(g => Array.IndexOf(KindOrder, g.Key) is var i && i < 0 ? int.MaxValue : i)
            .ThenBy(g => g.Key, StringComparer.Ordinal);

        foreach (var kindGroup in byKind)
        {
            var byAcc = kindGroup
                .GroupBy(m => (string)m["accessibility"]!)
                .OrderBy(g => Array.IndexOf(AccessibilityOrder, g.Key) is var i && i < 0 ? int.MaxValue : i)
                .ThenBy(g => g.Key, StringComparer.Ordinal);

            var accessibilityBucket = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var accGroup in byAcc)
            {
                var stripped = accGroup
                    .Select(m =>
                    {
                        // Drop kind/accessibility — they're now the dict keys above.
                        var copy = new Dictionary<string, object?>(m, StringComparer.Ordinal);
                        copy.Remove("kind");
                        copy.Remove("accessibility");
                        return copy;
                    })
                    .ToList();
                accessibilityBucket[accGroup.Key] = stripped;
            }
            grouped[kindGroup.Key] = accessibilityBucket;
        }

        return grouped;
    }
}
