using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Provides methods for outlining files, directories, and namespaces within a solution.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class OutlineService : IRtService
{
    // Public-first; private last. Mirrors how a reader scans an unfamiliar type.
    private readonly string[] AccessibilityOrder =
        ["Public", "Internal", "ProtectedOrInternal", "Protected", "ProtectedAndInternal", "Private", "NotApplicable"];

    // C# convention order — Field > Property > Event > Method > NamedType (nested types last).
    // Anything not in this list (rare — e.g. ErrorType) sorts alphabetically at the end.
    private readonly string[] KindOrder = ["Field", "Property", "Event", "Method", "NamedType"];

    private static readonly Regex XmlSummaryRegex =
        new(@"<summary>(.*?)</summary>", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Outlines a file within a solution asynchronously.
    /// </summary>
    internal async Task<List<Dictionary<string, object?>>> OutlineFileAsync(
        Solution solution,
        string filePath,
        bool summary = false)
    {
        var fullPath = Path.GetFullPath(filePath.Replace('/', Path.DirectorySeparatorChar));
        var results = new List<Dictionary<string, object?>>();

        foreach (var project in solution.Projects)
        foreach (var doc in project.Documents)
        {
            if (doc.FilePath is null) continue;
            var docPath = Path.GetFullPath(doc.FilePath.Replace('/', Path.DirectorySeparatorChar));
            if (!string.Equals(docPath, fullPath, StringComparison.OrdinalIgnoreCase))
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

            if (summary)
                declaredSymbols = declaredSymbols.Where(t => t.ContainingType is null);

            foreach (var type in declaredSymbols)
                results.Add(summary ? BuildTypeSummary(type) : BuildTypeOutline(type));
        }

        return results;
    }

    /// <summary>
    /// Outlines a directory within a solution asynchronously.
    /// </summary>
    internal async Task<List<Dictionary<string, object?>>> OutlineDirectoryAsync(
        Solution solution,
        string directoryPath,
        bool summary = false)
    {
        var fullDir = Path.GetFullPath(directoryPath.Replace('/', Path.DirectorySeparatorChar));
        var prefix = fullDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var results = new List<Dictionary<string, object?>>();

        foreach (var project in solution.Projects)
        foreach (var doc in project.Documents)
        {
            if (doc.FilePath is null) continue;
            var docPath = Path.GetFullPath(doc.FilePath);
            if (!docPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

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

            if (summary)
                types = types.Where(t => t.ContainingType is null);

            foreach (var type in types)
                results.Add(summary ? BuildTypeSummary(type) : BuildTypeOutline(type));
        }

        return results;
    }

    /// <summary>
    /// Outlines a namespace within a solution asynchronously.
    /// </summary>
    internal async Task<List<Dictionary<string, object?>>> OutlineNamespaceAsync(
        Solution solution,
        string namespaceName,
        bool summary = false)
    {
        var results = new List<Dictionary<string, object?>>();
        var seen = new HashSet<string>();

        var isPrefix = namespaceName.EndsWith(".*", StringComparison.Ordinal);
        var lookupName = isPrefix ? namespaceName[..^2] : namespaceName;

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            var nsSymbols = isPrefix
                ? FindNamespaceTypesByPrefix(compilation.GlobalNamespace, lookupName)
                : FindNamespaceTypes(compilation.GlobalNamespace, lookupName);

            if (summary)
                nsSymbols = nsSymbols.Where(t => t.ContainingType is null);

            foreach (var type in nsSymbols)
            {
                var key = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                if (seen.Add(key))
                    results.Add(summary ? BuildTypeSummary(type) : BuildTypeOutline(type));
            }
        }

        if (results.Count == 0)
            throw new InvalidOperationException($"No types found in namespace: {namespaceName}");

        return results;
    }

    /// <summary>
    /// Outlines files matching a glob pattern within a solution asynchronously.
    /// </summary>
    internal async Task<List<Dictionary<string, object?>>> OutlineGlobAsync(
        Solution solution,
        string globPattern,
        bool summary = false)
    {
        var matchingPaths = ExpandGlob(globPattern);
        var results = new List<Dictionary<string, object?>>();

        foreach (var project in solution.Projects)
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

            if (summary)
                types = types.Where(t => t.ContainingType is null);

            foreach (var type in types)
                results.Add(summary ? BuildTypeSummary(type) : BuildTypeOutline(type));
        }

        return results;
    }

    /// <summary>
    /// Outlines files matching a regular expression pattern within a solution asynchronously.
    /// </summary>
    internal async Task<List<Dictionary<string, object?>>> OutlineRegexAsync(
        Solution solution,
        string regexPattern,
        bool summary = false)
    {
        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
        var results = new List<Dictionary<string, object?>>();

        foreach (var project in solution.Projects)
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

            if (summary)
                types = types.Where(t => t.ContainingType is null);

            foreach (var type in types)
                results.Add(summary ? BuildTypeSummary(type) : BuildTypeOutline(type));
        }

        return results;
    }

    /// <summary>
    /// Finds and outlines namespace types within a solution.
    /// </summary>
    internal IEnumerable<INamedTypeSymbol> FindNamespaceTypes(
        INamespaceSymbol ns,
        string targetName)
    {
        if (ns.ToDisplayString() == targetName)
            foreach (var type in ns.GetTypeMembers())
                yield return type;

        foreach (var child in ns.GetNamespaceMembers())
        foreach (var type in FindNamespaceTypes(child, targetName))
            yield return type;
    }

    private IEnumerable<INamedTypeSymbol> FindNamespaceTypesByPrefix(
        INamespaceSymbol ns,
        string prefix)
    {
        var displayName = ns.ToDisplayString();
        if (displayName == prefix || displayName.StartsWith(prefix + ".", StringComparison.Ordinal))
            foreach (var type in ns.GetTypeMembers())
                yield return type;

        foreach (var child in ns.GetNamespaceMembers())
        foreach (var type in FindNamespaceTypesByPrefix(child, prefix))
            yield return type;
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
            return
            [
                .. Directory.EnumerateFiles(baseDir, filePattern, SearchOption.AllDirectories)
                    .Select(Path.GetFullPath)
            ];
        }
        else
        {
            var dir = Path.GetFullPath(Path.GetDirectoryName(pattern) ?? Environment.CurrentDirectory);
            var filePattern = Path.GetFileName(pattern);
            if (!Directory.Exists(dir)) return [];
            return
            [
                .. Directory.EnumerateFiles(dir, filePattern, SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFullPath)
            ];
        }
    }

    /// <summary>
    /// Builds an outline for a named type within a solution.
    /// </summary>
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
            ["lineTo"] = lineTo
        };

        SymbolFormatter.AddTypeHierarchyInfo(outline, type);

        // Group members by kind, then accessibility, to save tokens (kind/accessibility appear
        // once per group instead of on every member). Each member entry strips those redundant
        // keys but keeps name + signature + lineFrom/lineTo.
        outline["members"] = GroupMembersByKindAndAccessibility(
            SymbolFormatter.ExtractMembers(type, false));

        return outline;
    }

    /// <summary>
    /// Generates a summary for a named type's build type within a solution.
    /// </summary>
    internal Dictionary<string, object?> BuildTypeSummary(INamedTypeSymbol type)
    {
        var (lineFrom, lineTo) = SymbolFormatter.GetSourceLineRange(type);
        var summary = new Dictionary<string, object?>
        {
            ["fqn"] = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            ["kind"] = type.TypeKind.ToString(),
            ["lineFrom"] = lineFrom,
            ["lineTo"] = lineTo
        };

        var xml = type.GetDocumentationCommentXml();
        if (!string.IsNullOrWhiteSpace(xml))
        {
            var match = XmlSummaryRegex.Match(xml);
            if (match.Success)
            {
                var desc = Regex.Replace(match.Groups[1].Value.Trim(), @"\s+", " ");
                desc = new string(desc.Where(c => !char.IsControl(c)).ToArray());
                if (!string.IsNullOrEmpty(desc))
                    summary["description"] = desc;
            }
        }

        return summary;
    }

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
