using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynTools.Core;

internal static class MakeInstanceCore
{
    internal static async Task RunAsync(Solution sln, string methodSpec, bool dryRun)
    {
        var originalSolution = sln;

        var (typeName, methodName) = ParseMethodSpec(methodSpec);

        var methodSymbol = await FindStaticMethodAsync(sln, typeName, methodName)
            ?? throw new InvalidOperationException($"Static method '{methodSpec}' not found in solution.");

        Console.Error.WriteLine($"Found method '{methodSymbol.ToDisplayString()}'. Collecting call sites...");

        var refs = await SymbolFinder.FindReferencesAsync(methodSymbol, sln);
        var callSitesByDoc = GroupCallSitesByDocument(refs);

        int total = callSitesByDoc.Values.Sum(l => l.Count);
        Console.Error.WriteLine($"Found {total} call site(s). Analysing reachability...");

        sln = await RemoveStaticModifierAsync(sln, methodSymbol);

        // Re-resolve the target type in the updated solution's compilation so that
        // SymbolEqualityComparer comparisons work against the new semantic models.
        var updatedTargetType = await FindTypeSymbolAsync(sln, typeName)
            ?? methodSymbol.ContainingType;

        var (newSln, skipped) = await RewriteCallSitesAsync(sln, updatedTargetType, typeName, callSitesByDoc);
        sln = newSln;

        await SolutionWriter.ApplyChangesAsync(originalSolution, sln, dryRun);

        PrintSummary(total, skipped);
    }

    // ── Parsing ──────────────────────────────────────────────────────────────

    private static (string TypeName, string MethodName) ParseMethodSpec(string spec)
    {
        var lastDot = spec.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == spec.Length - 1)
            throw new InvalidOperationException(
                $"--method must be in the form Type.Method, got: '{spec}'");

        return (spec[..lastDot], spec[(lastDot + 1)..]);
    }

    // ── Symbol finding ───────────────────────────────────────────────────────

    private static async Task<INamedTypeSymbol?> FindTypeSymbolAsync(Solution sln, string typeName)
    {
        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var sym in compilation.GetSymbolsWithName(typeName, SymbolFilter.Type))
            {
                if (sym is INamedTypeSymbol namedType && namedType.Name == typeName)
                    return namedType;
            }
        }

        return null;
    }

    private static async Task<IMethodSymbol?> FindStaticMethodAsync(Solution sln, string typeName, string methodName)
    {
        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var sym in compilation.GetSymbolsWithName(typeName, SymbolFilter.Type))
            {
                if (sym is not INamedTypeSymbol typeSymbol) continue;

                var method = typeSymbol.GetMembers(methodName)
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(m => m.IsStatic);

                if (method != null)
                    return method;
            }
        }

        return null;
    }

    private static Dictionary<DocumentId, List<Location>> GroupCallSitesByDocument(
        IEnumerable<ReferencedSymbol> refs)
    {
        var result = new Dictionary<DocumentId, List<Location>>();
        foreach (var refSym in refs)
        {
            foreach (var loc in refSym.Locations)
            {
                if (!result.TryGetValue(loc.Document.Id, out var list))
                {
                    list = new List<Location>();
                    result[loc.Document.Id] = list;
                }

                list.Add(loc.Location);
            }
        }

        return result;
    }

    // ── Remove static modifier ───────────────────────────────────────────────

    private static async Task<Solution> RemoveStaticModifierAsync(Solution sln, IMethodSymbol methodSymbol)
    {
        var location = methodSymbol.Locations.FirstOrDefault(l => l.IsInSource)
            ?? throw new InvalidOperationException("No source location found for method declaration.");

        var docId = FindDocumentIdForLocation(sln, location)
            ?? throw new InvalidOperationException("Cannot locate source document for method declaration.");

        var doc = sln.GetDocument(docId)!;
        var root = (await doc.GetSyntaxRootAsync())!;

        var node = root.FindNode(location.SourceSpan);
        var methodDecl = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()
            ?? throw new InvalidOperationException("Cannot locate MethodDeclarationSyntax for the target method.");

        // Remove `static` token, fixing up whitespace so the token list stays clean
        var newModifiers = methodDecl.Modifiers
            .Where(t => !t.IsKind(SyntaxKind.StaticKeyword))
            .ToArray();

        var newTokenList = SyntaxFactory.TokenList(newModifiers);
        var newDecl = methodDecl.WithModifiers(newTokenList);

        root = root.ReplaceNode(methodDecl, newDecl);
        return sln.WithDocumentSyntaxRoot(docId, root);
    }

    // ── Call-site rewriting ──────────────────────────────────────────────────

    private static async Task<(Solution newSln, List<(string file, int line, string reason)> skipped)>
        RewriteCallSitesAsync(
            Solution sln,
            INamedTypeSymbol targetType,
            string typeName,
            Dictionary<DocumentId, List<Location>> callSitesByDoc)
    {
        var skipped = new List<(string file, int line, string reason)>();

        foreach (var (docId, locations) in callSitesByDoc)
        {
            var doc = sln.GetDocument(docId)!;
            var root = await doc.GetSyntaxRootAsync();
            if (root is null) continue;

            var semanticModel = await doc.GetSemanticModelAsync();
            if (semanticModel is null) continue;

            var replacements = new Dictionary<SyntaxNode, SyntaxNode>(ReferenceEqualityComparer.Instance);

            foreach (var loc in locations)
            {
                var refNode = root.FindNode(loc.SourceSpan);
                var invocation = refNode.AncestorsAndSelf()
                    .OfType<InvocationExpressionSyntax>()
                    .FirstOrDefault();

                if (invocation is null) continue;

                // Only handle Type.Method(...) call sites — skip if already an instance call
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) continue;

                var fileText = await doc.GetTextAsync();
                var lineNum = fileText.Lines.GetLineFromPosition(loc.SourceSpan.Start).LineNumber + 1;
                var filePath = doc.FilePath ?? doc.Name;

                var instanceExpr = FindReachableInstance(invocation, semanticModel, targetType);

                if (instanceExpr is null)
                {
                    const string reason = "no reachable instance";
                    Console.Error.WriteLine($"WARNING: {filePath}:{lineNum}: {reason} — skipped");
                    skipped.Add((filePath, lineNum, reason));
                    continue;
                }

                // Rewrite: Type.Method(...) → instance.Method(...)
                var newMemberAccess = memberAccess
                    .WithExpression(instanceExpr.WithTriviaFrom(memberAccess.Expression))
                    .WithTriviaFrom(memberAccess);

                replacements[invocation.Expression] = newMemberAccess;
            }

            if (replacements.Count == 0) continue;

            var newRoot = root.ReplaceNodes(
                replacements.Keys,
                (original, _) => replacements[original]);

            sln = sln.WithDocumentSyntaxRoot(docId, newRoot);
        }

        return (sln, skipped);
    }

    /// <summary>
    /// Finds a reachable instance of <paramref name="targetType"/> at <paramref name="callSite"/>.
    /// Priority: local variables → parameters → this (same-class instance context) → fields/properties.
    /// Returns null when conservatively no instance can be determined.
    /// </summary>
    private static ExpressionSyntax? FindReachableInstance(
        InvocationExpressionSyntax callSite,
        SemanticModel semanticModel,
        INamedTypeSymbol targetType)
    {
        var enclosingMember = callSite.Ancestors()
            .FirstOrDefault(n => n is MethodDeclarationSyntax
                              || n is ConstructorDeclarationSyntax
                              || n is PropertyDeclarationSyntax
                              || n is AccessorDeclarationSyntax);

        if (enclosingMember is null) return null;

        // 1. Local variables of the target type declared before this call site
        var localVar = FindLocalVariableName(callSite, semanticModel, targetType);
        if (localVar is not null)
            return SyntaxFactory.IdentifierName(localVar);

        // 2. Parameters of the target type in the enclosing member
        var param = FindParameterName(enclosingMember, semanticModel, targetType);
        if (param is not null)
            return SyntaxFactory.IdentifierName(param);

        // 3. Same-class call site: `this` when in an instance context
        var enclosingType = GetEnclosingTypeSymbol(enclosingMember, semanticModel);
        bool isInstance = IsInstanceContext(enclosingMember);

        if (enclosingType is not null && isInstance &&
            SymbolEqualityComparer.Default.Equals(enclosingType, targetType))
        {
            return SyntaxFactory.ThisExpression();
        }

        // 4. Instance field or property on the enclosing class that holds the target type
        if (enclosingType is not null && isInstance)
        {
            var fieldName = FindFieldOrPropertyName(enclosingType, targetType);
            if (fieldName is not null)
                return SyntaxFactory.IdentifierName(fieldName);
        }

        return null;
    }

    // ── Reachability helpers ─────────────────────────────────────────────────

    private static INamedTypeSymbol? GetEnclosingTypeSymbol(SyntaxNode memberNode, SemanticModel semanticModel)
    {
        var typeDecl = memberNode.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (typeDecl is null) return null;
        return semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
    }

    private static bool IsInstanceContext(SyntaxNode memberNode) =>
        memberNode switch
        {
            MethodDeclarationSyntax m =>
                !m.Modifiers.Any(t => t.IsKind(SyntaxKind.StaticKeyword)),
            ConstructorDeclarationSyntax c =>
                !c.Modifiers.Any(t => t.IsKind(SyntaxKind.StaticKeyword)),
            _ => true
        };

    private static string? FindLocalVariableName(
        InvocationExpressionSyntax callSite,
        SemanticModel semanticModel,
        INamedTypeSymbol targetType)
    {
        // Walk up to the nearest block that contains the call site
        var block = callSite.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
        if (block is null) return null;

        foreach (var decl in block.Statements
            .OfType<LocalDeclarationStatementSyntax>()
            .Where(d => d.SpanStart < callSite.SpanStart))
        {
            foreach (var variable in decl.Declaration.Variables)
            {
                // Use the declared symbol to get the resolved type (handles `var` correctly)
                var localSymbol = semanticModel.GetDeclaredSymbol(variable) as ILocalSymbol;
                if (localSymbol is null) continue;
                if (SymbolEqualityComparer.Default.Equals(localSymbol.Type, targetType))
                    return variable.Identifier.Text;
            }
        }

        return null;
    }

    private static string? FindParameterName(
        SyntaxNode enclosingMember,
        SemanticModel semanticModel,
        INamedTypeSymbol targetType)
    {
        ParameterListSyntax? paramList = enclosingMember switch
        {
            MethodDeclarationSyntax m => m.ParameterList,
            ConstructorDeclarationSyntax c => c.ParameterList,
            _ => null
        };

        if (paramList is null) return null;

        foreach (var param in paramList.Parameters)
        {
            // Use the declared symbol to get the fully resolved parameter type
            var paramSymbol = semanticModel.GetDeclaredSymbol(param) as IParameterSymbol;
            if (paramSymbol is null) continue;
            if (SymbolEqualityComparer.Default.Equals(paramSymbol.Type, targetType))
                return param.Identifier.Text;
        }

        return null;
    }

    private static string? FindFieldOrPropertyName(INamedTypeSymbol enclosingType, INamedTypeSymbol targetType)
    {
        foreach (var member in enclosingType.GetMembers())
        {
            if (member.IsStatic) continue;

            ITypeSymbol? memberType = member switch
            {
                IFieldSymbol f => f.Type,
                IPropertySymbol p => p.Type,
                _ => null
            };

            if (memberType is null) continue;
            if (SymbolEqualityComparer.Default.Equals(memberType, targetType))
                return member.Name;
        }

        return null;
    }

    // ── Document/location utilities ──────────────────────────────────────────

    private static DocumentId? FindDocumentIdForLocation(Solution sln, Location location)
    {
        foreach (var project in sln.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath != null &&
                    string.Equals(doc.FilePath, location.SourceTree?.FilePath, StringComparison.OrdinalIgnoreCase))
                    return doc.Id;
            }
        }

        return null;
    }

    // ── Output ───────────────────────────────────────────────────────────────

    private static void PrintSummary(int total, List<(string file, int line, string reason)> skipped)
    {
        int rewritten = total - skipped.Count;
        Console.WriteLine($"\nSummary: {total} call site(s) found, {rewritten} rewritten, {skipped.Count} skipped.");

        foreach (var (file, line, reason) in skipped)
            Console.WriteLine($"  SKIPPED {file}:{line} — {reason}");
    }
}
