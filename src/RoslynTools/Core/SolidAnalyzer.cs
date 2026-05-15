using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

internal record SrpViolation(
    string TypeName, string FilePath, int Line,
    int PublicMethodCount, int DependencyCount, string Detail);

internal record SolidSrpReport(IList<SrpViolation> Violations);

internal static class SolidAnalyzer
{
    public static async Task<SolidSrpReport> AnalyzeSrpAsync(
        Solution sln, int minMethods, int minDeps)
    {
        var violations = new List<SrpViolation>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync().ConfigureAwait(false);
            if (compilation is null) // coverage: unreachable via AdhocWorkspace; defensive guard for real MSBuild projects
                continue;

            foreach (var document in project.Documents)
            {
                var filePath = document.FilePath ?? string.Empty;

                if (FilePathFilters.IsGeneratedOrIntermediate(filePath))
                    continue;

                var root = await document.GetSyntaxRootAsync().ConfigureAwait(false);
                if (root is null) // coverage: unreachable via AdhocWorkspace; defensive guard for real MSBuild projects
                    continue;

                var model = compilation.GetSemanticModel(root.SyntaxTree);

                foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol symbol) // coverage: unreachable for well-formed C#; defensive guard
                        continue;

                    // Skip non-classes: interfaces, records, structs
                    if (symbol.TypeKind != TypeKind.Class)
                        continue;

                    // Skip abstract, static, record classes
                    if (symbol.IsAbstract || symbol.IsStatic || symbol.IsRecord)
                        continue;

                    // Deduplicate partial classes — first declaration encountered wins
                    if (!seen.Add(symbol))
                        continue;

                    var publicMethodCount = CountPublicOrdinaryMethods(symbol);
                    var dependencyCount = CountDistinctConstructorDeps(symbol);

                    if (publicMethodCount < minMethods || dependencyCount < minDeps)
                        continue;

                    var line = typeDecl.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var typeName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    var detail = $"{publicMethodCount} public methods, {dependencyCount} injected dependencies";

                    violations.Add(new SrpViolation(typeName, filePath, line,
                        publicMethodCount, dependencyCount, detail));
                }
            }
        }

        violations.Sort((a, b) => StringComparer.Ordinal.Compare(a.TypeName, b.TypeName));
        return new SolidSrpReport(violations);
    }

    private static int CountPublicOrdinaryMethods(INamedTypeSymbol symbol)
    {
        return symbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Count(m => m is { DeclaredAccessibility: Accessibility.Public, MethodKind: MethodKind.Ordinary });
    }

    private static int CountDistinctConstructorDeps(INamedTypeSymbol symbol)
    {
        var explicitCtors = symbol.InstanceConstructors
            .Where(c => !c.IsImplicitlyDeclared)
            .ToList();

        if (explicitCtors.Count == 0)
            return 0;

        // Pick the constructor with the most parameters
        var maxCtor = explicitCtors
            .OrderByDescending(c => c.Parameters.Length)
            .First();

        return maxCtor.Parameters
            .Select(p => p.Type.OriginalDefinition)
            .Distinct<ITypeSymbol>(SymbolEqualityComparer.Default)
            .Count();
    }
}
