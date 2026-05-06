using System.Text.RegularExpressions;
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
        var documentFound = false;

        foreach (var project in solution.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (!string.Equals(doc.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                documentFound = true;

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

        if (!documentFound)
            throw new InvalidOperationException($"No types found in file: {fullPath}");

        return results;
    }

    internal async Task<List<Dictionary<string, object?>>> OutlineNamespaceAsync(
        Solution solution,
        string namespaceName)
    {
        var results = new List<Dictionary<string, object?>>();
        var seen = new HashSet<string>();

        bool isPrefix = namespaceName.EndsWith(".*", StringComparison.Ordinal);
        var lookupName = isPrefix ? namespaceName[..^2] : namespaceName;

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            var nsSymbols = isPrefix
                ? FindNamespaceTypesByPrefix(compilation.GlobalNamespace, lookupName)
                : FindNamespaceTypes(compilation.GlobalNamespace, lookupName);

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

    internal async Task<List<Dictionary<string, object?>>> OutlineGlobAsync(
        Solution solution,
        string globPattern)
    {
        var matchingPaths = ExpandGlob(globPattern);
        var results = new List<Dictionary<string, object?>>();

        foreach (var project in solution.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath == null) continue;
                if (!matchingPaths.Contains(Path.GetFullPath(doc.FilePath))) continue;

                var semanticModel = await doc.GetSemanticModelAsync();
                if (semanticModel == null) continue;

                var root = await doc.GetSyntaxRootAsync();
                if (root == null) continue;

                var types = root.DescendantNodes()
                    .Select(n => semanticModel.GetDeclaredSymbol(n))
                    .Where(s => s is INamedTypeSymbol)
                    .Cast<INamedTypeSymbol>()
                    .Distinct(SymbolEqualityComparer.Default)
                    .Cast<INamedTypeSymbol>();

                foreach (var type in types)
                    results.Add(BuildTypeOutline(type));
            }
        }

        return results;
    }

    internal async Task<List<Dictionary<string, object?>>> OutlineRegexAsync(
        Solution solution,
        string regexPattern)
    {
        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
        var results = new List<Dictionary<string, object?>>();

        foreach (var project in solution.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath == null || !regex.IsMatch(doc.FilePath)) continue;

                var semanticModel = await doc.GetSemanticModelAsync();
                if (semanticModel == null) continue;

                var root = await doc.GetSyntaxRootAsync();
                if (root == null) continue;

                var types = root.DescendantNodes()
                    .Select(n => semanticModel.GetDeclaredSymbol(n))
                    .Where(s => s is INamedTypeSymbol)
                    .Cast<INamedTypeSymbol>()
                    .Distinct(SymbolEqualityComparer.Default)
                    .Cast<INamedTypeSymbol>();

                foreach (var type in types)
                    results.Add(BuildTypeOutline(type));
            }
        }

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

    private IEnumerable<INamedTypeSymbol> FindNamespaceTypesByPrefix(
        INamespaceSymbol ns,
        string prefix)
    {
        var displayName = ns.ToDisplayString();
        if (displayName == prefix || displayName.StartsWith(prefix + ".", StringComparison.Ordinal))
        {
            foreach (var type in ns.GetTypeMembers())
                yield return type;
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in FindNamespaceTypesByPrefix(child, prefix))
                yield return type;
        }
    }

    private static HashSet<string> ExpandGlob(string pattern)
    {
        pattern = pattern.Replace('/', Path.DirectorySeparatorChar);

        if (pattern.Contains("**"))
        {
            var starIdx = pattern.IndexOf("**", StringComparison.Ordinal);
            var baseDir = Path.GetFullPath(pattern[..starIdx].TrimEnd(Path.DirectorySeparatorChar));
            var afterStars = pattern[(starIdx + 2)..].TrimStart(Path.DirectorySeparatorChar);
            var filePattern = afterStars.Length > 0 ? afterStars : "*";

            if (!Directory.Exists(baseDir)) return [];
            return [.. Directory.EnumerateFiles(baseDir, filePattern, SearchOption.AllDirectories)
                .Select(Path.GetFullPath)];
        }
        else
        {
            var dir = Path.GetFullPath(Path.GetDirectoryName(pattern) ?? Environment.CurrentDirectory);
            var filePattern = Path.GetFileName(pattern);
            if (!Directory.Exists(dir)) return [];
            return [.. Directory.EnumerateFiles(dir, filePattern, SearchOption.TopDirectoryOnly)
                .Select(Path.GetFullPath)];
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
