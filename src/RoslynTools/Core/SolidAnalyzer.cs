using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

internal record SrpViolation(
    string TypeName, string FilePath, int Line,
    int PublicMethodCount, int DependencyCount, string Detail);

internal record SolidSrpReport(IList<SrpViolation> Violations);

internal record IspFatInterface(
    string InterfaceName, string FilePath, int Line, int MemberCount);

internal record IspUnimplementedMember(
    string TypeName, string MemberName, string InterfaceName,
    string FilePath, int Line);

internal record SolidIspReport(
    IList<IspFatInterface> FatInterfaces,
    IList<IspUnimplementedMember> UnimplementedMembers);

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

    public static async Task<SolidIspReport> AnalyzeIspAsync(
        Solution sln, int minMembers)
    {
        var fatInterfaces = new List<IspFatInterface>();
        var unimplementedMembers = new List<IspUnimplementedMember>();
        var seenInterfaces = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var seenClasses = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync().ConfigureAwait(false);
            if (compilation is null) // coverage: unreachable via AdhocWorkspace; defensive guard
                continue;

            foreach (var document in project.Documents)
            {
                var filePath = document.FilePath ?? string.Empty;

                if (FilePathFilters.IsGeneratedOrIntermediate(filePath))
                    continue;

                var root = await document.GetSyntaxRootAsync().ConfigureAwait(false);
                if (root is null) // coverage: unreachable via AdhocWorkspace; defensive guard
                    continue;

                var model = compilation.GetSemanticModel(root.SyntaxTree);

                foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol symbol) // coverage: defensive guard
                        continue;

                    if (symbol.TypeKind == TypeKind.Interface)
                    {
                        if (!seenInterfaces.Add(symbol))
                            continue;

                        var declaredCount = CountDeclaredInterfaceMembers(symbol);
                        if (declaredCount < minMembers)
                            continue;

                        var line = typeDecl.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        var name = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                        fatInterfaces.Add(new IspFatInterface(name, filePath, line, declaredCount));
                    }
                    else if (symbol.TypeKind == TypeKind.Class && !symbol.IsAbstract)
                    {
                        if (!seenClasses.Add(symbol))
                            continue;

                        CollectUnimplementedInterfaceMembers(
                            symbol, typeDecl, filePath, unimplementedMembers);
                    }
                }
            }
        }

        fatInterfaces.Sort((a, b) => StringComparer.Ordinal.Compare(a.InterfaceName, b.InterfaceName));
        unimplementedMembers.Sort((a, b) =>
        {
            var byType = StringComparer.Ordinal.Compare(a.TypeName, b.TypeName);
            if (byType != 0) return byType;
            return StringComparer.Ordinal.Compare(a.MemberName, b.MemberName);
        });

        return new SolidIspReport(fatInterfaces, unimplementedMembers);
    }

    private static int CountDeclaredInterfaceMembers(INamedTypeSymbol interfaceSymbol)
    {
        // Count only members directly declared on this interface — exclude inherited.
        // Members of TypeKind.Property/Event are top-level here; their accessor methods are not.
        return interfaceSymbol.GetMembers()
            .Count(m => m switch
            {
                IMethodSymbol method => method.MethodKind == MethodKind.Ordinary,
                IPropertySymbol => true,
                IEventSymbol => true,
                _ => false
            });
    }

    private static void CollectUnimplementedInterfaceMembers(
        INamedTypeSymbol classSymbol, TypeDeclarationSyntax typeDecl, string filePath,
        List<IspUnimplementedMember> results)
    {
        if (classSymbol.AllInterfaces.IsDefaultOrEmpty)
            return;

        var typeName = classSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        foreach (var iface in classSymbol.AllInterfaces)
        {
            foreach (var ifaceMember in iface.GetMembers())
            {
                if (ifaceMember is not (IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol or IEventSymbol))
                    continue;

                var impl = classSymbol.FindImplementationForInterfaceMember(ifaceMember);
                if (impl is null)
                    continue;

                // Only flag implementations declared directly on this class
                if (!SymbolEqualityComparer.Default.Equals(impl.ContainingType, classSymbol))
                    continue;

                // Skip compiler-synthesized members (e.g. record-generated Equals/GetHashCode)
                // — their DeclaringSyntaxReferences point at the type declaration,
                // which can yield spurious throw matches from sibling members.
                if (impl.IsImplicitlyDeclared)
                    continue;

                if (!ImplementationThrowsNotImplemented(impl))
                    continue;

                var line = typeDecl.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var memberName = impl.Name;
                var ifaceName = iface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                results.Add(new IspUnimplementedMember(typeName, memberName, ifaceName, filePath, line));
            }
        }
    }

    private static bool ImplementationThrowsNotImplemented(ISymbol implMember)
    {
        foreach (var declRef in implMember.DeclaringSyntaxReferences)
        {
            var syntax = declRef.GetSyntax();
            if (SyntaxThrowsNotImplemented(syntax))
                return true;
        }
        return false;
    }

    private static bool SyntaxThrowsNotImplemented(SyntaxNode node)
    {
        // Match: => throw new System.NotImplementedException()/NotSupportedException()
        // Match: { throw new ...(); }  (single statement body)
        // Accessors come through as AccessorDeclarationSyntax whose body or expressionBody applies.
        foreach (var throwExpr in node.DescendantNodes().OfType<ThrowExpressionSyntax>())
        {
            if (IsNotImplementedOrSupportedCreation(throwExpr.Expression))
                return true;
        }
        foreach (var throwStmt in node.DescendantNodes().OfType<ThrowStatementSyntax>())
        {
            if (throwStmt.Expression is { } expr && IsNotImplementedOrSupportedCreation(expr))
                return true;
        }
        return false;
    }

    private static bool IsNotImplementedOrSupportedCreation(ExpressionSyntax expr)
    {
        if (expr is not ObjectCreationExpressionSyntax oc)
            return false;
        var typeName = oc.Type.ToString();
        return typeName.EndsWith("NotImplementedException", StringComparison.Ordinal)
            || typeName.EndsWith("NotSupportedException", StringComparison.Ordinal);
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
