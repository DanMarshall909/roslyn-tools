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

        // Group reference locations by document (from the original solution)
        var locationsByDoc = refs
            .SelectMany(r => r.Locations)
            .GroupBy(loc => loc.Document.Id)
            .ToDictionary(g => g.Key, g => g.Select(l => l.Location.SourceSpan).ToList());

        int total = locationsByDoc.Values.Sum(l => l.Count);
        Console.Error.WriteLine($"Found {total} call site(s). Analysing reachability...");

        // Find the document containing the method declaration
        var declLocation = methodSymbol.Locations.FirstOrDefault(l => l.IsInSource)
            ?? throw new InvalidOperationException("No source location for method declaration.");
        var declDocId = FindDocumentIdForLocation(sln, declLocation)
            ?? throw new InvalidOperationException("Cannot locate source document for method declaration.");

        // Process all documents that have call sites plus the declaration document
        var allDocIds = new HashSet<DocumentId>(locationsByDoc.Keys) { declDocId };
        var skipped = new List<(string file, int line, string reason)>();

        foreach (var docId in allDocIds)
        {
            var doc = sln.GetDocument(docId)!;
            var root = (await doc.GetSyntaxRootAsync())!;
            var semanticModel = await doc.GetSemanticModelAsync();
            if (semanticModel is null) continue;

            locationsByDoc.TryGetValue(docId, out var callSpans);
            var isDeclarationDoc = docId == declDocId;

            var (newRoot, docSkipped) = TransformDocument(
                root, semanticModel, methodSymbol, methodSymbol.ContainingType,
                callSpans ?? [], isDeclarationDoc, doc);

            foreach (var s in docSkipped) skipped.Add(s);

            sln = sln.WithDocumentSyntaxRoot(docId, newRoot);
        }

        await SolutionWriter.ApplyChangesAsync(originalSolution, sln, dryRun);

        PrintSummary(total, skipped);
    }

    // ── Document transformation ──────────────────────────────────────────────

    /// <summary>
    /// Transforms one document in a single ReplaceNodes pass:
    ///   • Removes the `static` modifier from the method declaration (if this is the declaration doc)
    ///   • Rewrites qualifying call sites from Type.Method(...) to instance.Method(...)
    ///   • Warns and skips call sites where no instance is reachable
    /// </summary>
    private static (SyntaxNode newRoot, List<(string file, int line, string reason)> skipped)
        TransformDocument(
            SyntaxNode root,
            SemanticModel semanticModel,
            IMethodSymbol methodSymbol,
            INamedTypeSymbol targetType,
            List<Microsoft.CodeAnalysis.Text.TextSpan> callSpans,
            bool isDeclarationDoc,
            Document doc)
    {
        var skipped = new List<(string file, int line, string reason)>();
        var replacements = new Dictionary<SyntaxNode, SyntaxNode>(ReferenceEqualityComparer.Instance);

        // ── 1. Remove `static` from the declaration ──────────────────────────
        if (isDeclarationDoc)
        {
            var declLocation = methodSymbol.Locations.FirstOrDefault(l => l.IsInSource);
            if (declLocation is not null)
            {
                var declNode = root.FindNode(declLocation.SourceSpan);
                var methodDecl = declNode.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                if (methodDecl is not null)
                {
                    var newModifiers = methodDecl.Modifiers
                        .Where(t => !t.IsKind(SyntaxKind.StaticKeyword))
                        .ToArray();
                    var newDecl = methodDecl.WithModifiers(SyntaxFactory.TokenList(newModifiers));
                    replacements[methodDecl] = newDecl;
                }
            }
        }

        // ── 2. Rewrite call sites ────────────────────────────────────────────
        var fileText = doc.GetTextAsync().GetAwaiter().GetResult();
        var filePath = doc.FilePath ?? doc.Name;

        foreach (var span in callSpans)
        {
            var refNode = root.FindNode(span);
            var invocation = refNode.AncestorsAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault();

            if (invocation is null) continue;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) continue;

            var lineNum = fileText.Lines.GetLineFromPosition(span.Start).LineNumber + 1;

            var instanceExpr = FindReachableInstance(invocation, semanticModel, targetType);

            if (instanceExpr is null)
            {
                Console.Error.WriteLine($"WARNING: {filePath}:{lineNum}: no reachable instance — skipped");
                skipped.Add((filePath, lineNum, "no reachable instance"));
                continue;
            }

            var newMemberAccess = memberAccess
                .WithExpression(instanceExpr.WithTriviaFrom(memberAccess.Expression))
                .WithTriviaFrom(memberAccess);

            if (!replacements.ContainsKey(invocation.Expression))
                replacements[invocation.Expression] = newMemberAccess;
        }

        if (replacements.Count == 0)
            return (root, skipped);

        var newRoot = root.ReplaceNodes(
            replacements.Keys,
            (original, _) => replacements[original]);

        return (newRoot, skipped);
    }

    // ── Reachability analysis ────────────────────────────────────────────────

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
        var block = callSite.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
        if (block is null) return null;

        foreach (var decl in block.Statements
            .OfType<LocalDeclarationStatementSyntax>()
            .Where(d => d.SpanStart < callSite.SpanStart))
        {
            foreach (var variable in decl.Declaration.Variables)
            {
                // GetDeclaredSymbol resolves `var` to the actual type
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

    // ── Symbol finding ───────────────────────────────────────────────────────

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

    // ── Parsing ──────────────────────────────────────────────────────────────

    private static (string TypeName, string MethodName) ParseMethodSpec(string spec)
    {
        var lastDot = spec.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == spec.Length - 1)
            throw new InvalidOperationException(
                $"--method must be in the form Type.Method, got: '{spec}'");

        return (spec[..lastDot], spec[(lastDot + 1)..]);
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
