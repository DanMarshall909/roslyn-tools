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

internal record DipConcreteCtorParam(
    string TypeName, string ParamName, string ConcreteType,
    string FilePath, int Line);

internal record DipInlineNewCall(
    string TypeName, string MemberName, string ConcreteType,
    string FilePath, int Line);

internal record SolidDipReport(
    IList<DipConcreteCtorParam> ConcreteConstructorParams,
    IList<DipInlineNewCall> InlineNewCalls);

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

    public static async Task<SolidDipReport> AnalyzeDipAsync(Solution sln)
    {
        var ctorParams = new List<DipConcreteCtorParam>();
        var newCalls = new List<DipInlineNewCall>();
        var seenTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync().ConfigureAwait(false);
            if (compilation is null) // coverage: defensive guard
                continue;

            foreach (var document in project.Documents)
            {
                var filePath = document.FilePath ?? string.Empty;

                if (FilePathFilters.IsGeneratedOrIntermediate(filePath))
                    continue;

                var root = await document.GetSyntaxRootAsync().ConfigureAwait(false);
                if (root is null) // coverage: defensive guard
                    continue;

                var model = compilation.GetSemanticModel(root.SyntaxTree);

                foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol symbol) // coverage: defensive guard
                        continue;

                    // Only concrete classes (skip interfaces, abstract, static, records, structs)
                    if (symbol.TypeKind != TypeKind.Class)
                        continue;
                    if (symbol.IsAbstract || symbol.IsStatic || symbol.IsRecord)
                        continue;

                    if (!seenTypes.Add(symbol))
                        continue;

                    var typeName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

                    // (1) Constructor concrete params
                    foreach (var ctor in symbol.InstanceConstructors)
                    {
                        if (ctor.IsImplicitlyDeclared)
                            continue;

                        foreach (var param in ctor.Parameters)
                        {
                            if (!IsConcreteDependencyType(param.Type))
                                continue;

                            var line = GetDeclarationLine(param);
                            var concreteName = param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                            ctorParams.Add(new DipConcreteCtorParam(
                                typeName, param.Name, concreteName, filePath, line));
                        }
                    }

                    // (2) Inline new calls — skip factory/builder/provider classes
                    if (IsFactoryStyleClass(symbol.Name))
                        continue;

                    foreach (var newExpr in typeDecl.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                    {
                        var typeInfo = model.GetTypeInfo(newExpr.Type);
                        if (typeInfo.Type is not INamedTypeSymbol newType)
                            continue;

                        if (!IsExternalConcreteDependency(newType))
                            continue;

                        var memberName = FindEnclosingMemberName(newExpr) ?? "<unknown>";
                        var line = newExpr.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        var concreteName = newType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                        newCalls.Add(new DipInlineNewCall(
                            typeName, memberName, concreteName, filePath, line));
                    }
                }
            }
        }

        ctorParams.Sort((a, b) =>
        {
            var byType = StringComparer.Ordinal.Compare(a.TypeName, b.TypeName);
            if (byType != 0) return byType;
            return StringComparer.Ordinal.Compare(a.ParamName, b.ParamName);
        });
        newCalls.Sort((a, b) =>
        {
            var byType = StringComparer.Ordinal.Compare(a.TypeName, b.TypeName);
            if (byType != 0) return byType;
            return StringComparer.Ordinal.Compare(a.MemberName, b.MemberName);
        });

        return new SolidDipReport(ctorParams, newCalls);
    }

    private static bool IsConcreteDependencyType(ITypeSymbol type)
    {
        // Must be a concrete class to qualify
        if (type is not INamedTypeSymbol named)
            return false;
        if (named.TypeKind != TypeKind.Class)
            return false;
        if (named.IsAbstract || named.IsRecord)
            return false;

        // Skip BCL types (System.*) — they are primitives, exceptions, DTOs, delegates etc.
        if (IsSystemNamespaceType(named))
            return false;

        return true;
    }

    private static bool IsExternalConcreteDependency(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class)
            return false;
        if (type.IsAbstract || type.IsRecord || type.IsValueType)
            return false;

        // Exclude Exception-derived
        if (IsExceptionDerived(type))
            return false;

        // Exclude BCL types
        if (IsSystemNamespaceType(type))
            return false;

        return true;
    }

    private static bool IsExceptionDerived(INamedTypeSymbol type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == "System.Exception")
                return true;
        }
        return false;
    }

    private static bool IsSystemNamespaceType(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace;
        while (ns is { IsGlobalNamespace: false })
        {
            if (ns.Name == "System")
            {
                // True if the root or any ancestor is exactly "System"
                if (ns.ContainingNamespace is null || ns.ContainingNamespace.IsGlobalNamespace)
                    return true;
            }
            ns = ns.ContainingNamespace;
        }
        return false;
    }

    private static bool IsFactoryStyleClass(string name)
    {
        return name.EndsWith("Factory", StringComparison.Ordinal)
            || name.EndsWith("Builder", StringComparison.Ordinal)
            || name.EndsWith("Provider", StringComparison.Ordinal);
    }

    private static int GetDeclarationLine(ISymbol symbol)
    {
        var loc = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (loc is null)
            return 0;
        return loc.GetLineSpan().StartLinePosition.Line + 1;
    }

    private static string? FindEnclosingMemberName(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case MethodDeclarationSyntax m: return m.Identifier.Text;
                case ConstructorDeclarationSyntax c: return $"{c.Identifier.Text}.ctor";
                case PropertyDeclarationSyntax p: return p.Identifier.Text;
                case AccessorDeclarationSyntax a when a.Parent?.Parent is PropertyDeclarationSyntax pp:
                    return $"{pp.Identifier.Text}.{a.Keyword.Text}";
                case LocalFunctionStatementSyntax lf: return lf.Identifier.Text;
            }
        }
        return null;
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
