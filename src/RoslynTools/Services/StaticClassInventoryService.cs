using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Models;

namespace RoslynTools.Services;

internal static class StaticClassInventoryService
{
    internal static async Task<StaticClassInventoryReport> AnalyzeAsync(Solution solution)
    {
        // 1. Collect all static class symbols across the solution.
        var staticClasses = new List<INamedTypeSymbol>();

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation is null)
                continue;

            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel is null)
                    continue;

                foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (semanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol typeSymbol)
                        continue;

                    if (typeSymbol.IsStatic)
                        staticClasses.Add(typeSymbol);
                }
            }
        }

        // Build a lookup set of static class FQNs for quick membership test.
        var staticClassFqns = new HashSet<string>(
            staticClasses.Select(s => s.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)),
            StringComparer.Ordinal);

        // 2. For each static class, scan the whole solution for call sites.
        var entries = new List<StaticClassEntry>();

        foreach (var staticClass in staticClasses)
        {
            var callerStaticFqns = new SortedSet<string>(StringComparer.Ordinal);
            var callerInstanceFqns = new SortedSet<string>(StringComparer.Ordinal);
            var totalCallSites = 0;

            var classFqn = staticClass.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

            // Collect the static members to look for call sites of.
            var members = staticClass.GetMembers()
                .Where(m => !m.IsImplicitlyDeclared && m.IsStatic)
                .ToList();

            foreach (var project in solution.Projects)
            {
                foreach (var document in project.Documents)
                {
                    var root = await document.GetSyntaxRootAsync();
                    if (root is null)
                        continue;

                    var semanticModel = await document.GetSemanticModelAsync();
                    if (semanticModel is null)
                        continue;

                    foreach (var nameNode in root.DescendantNodes().OfType<SimpleNameSyntax>())
                    {
                        // Skip nameof() expressions.
                        if (IsWithinNameof(nameNode))
                            continue;

                        var symbolInfo = semanticModel.GetSymbolInfo(nameNode);
                        var referencedSymbol = symbolInfo.Symbol ?? (symbolInfo.CandidateSymbols.Length == 1
                            ? symbolInfo.CandidateSymbols[0]
                            : null);

                        if (referencedSymbol is null)
                            continue;

                        // Check if this name refers to a member of our static class.
                        var containingType = referencedSymbol.ContainingType;
                        if (containingType is null)
                            continue;

                        var containingFqn = containingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                        if (!string.Equals(containingFqn, classFqn, StringComparison.Ordinal))
                            continue;

                        // Found a call site. Determine the caller's enclosing type.
                        var callerType = GetEnclosingNamedType(semanticModel, nameNode.SpanStart);
                        if (callerType is null)
                            continue;

                        // Don't count self-references.
                        var callerFqn = callerType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                        if (string.Equals(callerFqn, classFqn, StringComparison.Ordinal))
                            continue;

                        totalCallSites++;

                        if (callerType.IsStatic)
                            callerStaticFqns.Add(callerFqn);
                        else
                            callerInstanceFqns.Add(callerFqn);
                    }
                }
            }

            // Determine file path from first location.
            var locationFile = staticClass.Locations
                .Where(loc => loc.IsInSource && loc.SourceTree is not null)
                .Select(loc => loc.SourceTree!.FilePath)
                .FirstOrDefault() ?? string.Empty;

            // Count non-implicitly-declared members.
            var memberCount = staticClass.GetMembers()
                .Count(m => !m.IsImplicitlyDeclared);

            var difficulty = ClassifyDifficulty(callerStaticFqns.Count);

            entries.Add(new StaticClassEntry(
                Fqn: classFqn,
                File: locationFile,
                MemberCount: memberCount,
                CallSiteCount: totalCallSites,
                CalledFromStaticClasses: [.. callerStaticFqns],
                CalledFromInstanceClasses: [.. callerInstanceFqns],
                MigrationDifficulty: difficulty));
        }

        // 3. Sort: leaf < mid < hub, then callSiteCount asc.
        var sorted = entries
            .OrderBy(e => DifficultyRank(e.MigrationDifficulty))
            .ThenBy(e => e.CallSiteCount)
            .ToList();

        return new StaticClassInventoryReport(
            TotalStaticClasses: sorted.Count,
            Classes: sorted);
    }

    private static string ClassifyDifficulty(int staticCallerCount)
    {
        return staticCallerCount switch
        {
            0 => "leaf",
            <= 3 => "mid",
            _ => "hub",
        };
    }

    private static int DifficultyRank(string difficulty)
    {
        return difficulty switch
        {
            "leaf" => 0,
            "mid" => 1,
            _ => 2,
        };
    }

    private static INamedTypeSymbol? GetEnclosingNamedType(SemanticModel semanticModel, int position)
    {
        for (var current = semanticModel.GetEnclosingSymbol(position); current is not null; current = current.ContainingSymbol)
        {
            if (current is INamedTypeSymbol namedType
                && namedType.TypeKind is TypeKind.Class or TypeKind.Struct)
            {
                return namedType;
            }
        }

        return null;
    }

    private static bool IsWithinNameof(SyntaxNode node)
    {
        return node.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv =>
                inv.Expression is SimpleNameSyntax { Identifier.Text: "nameof" }
                || inv.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "nameof" });
    }
}
