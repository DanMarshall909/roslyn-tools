using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynTools.Core;

internal static class MakeStaticCore
{
    internal static async Task<int> RunAsync(Solution sln, string methodSpec, bool dryRun)
    {
        var (typeName, methodName) = ParseMethodSpec(methodSpec);

        var (methodSymbol, docId) = await FindMethodAndDocumentAsync(sln, typeName, methodName);

        var blockingReason = await CheckSafetyAsync(methodSymbol);
        if (blockingReason != null)
        {
            RtLog.Error("Cannot make static: {BlockingReason}", blockingReason);
            return 1;
        }

        var originalSolution = sln;
        sln = await AddStaticModifierAsync(sln, methodSymbol, docId);
        sln = await RewriteCallSitesAsync(sln, methodSymbol, typeName, originalSolution);

        await SolutionWriter.ApplyChangesAsync(originalSolution, sln, dryRun);
        return 0;
    }

    // ── Parsing ──────────────────────────────────────────────────────────────

    private static (string typeName, string methodName) ParseMethodSpec(string spec)
    {
        var dot = spec.LastIndexOf('.');
        if (dot <= 0 || dot >= spec.Length - 1)
            throw new InvalidOperationException(
                $"--method must be in the form <Type>.<Method>, got: '{spec}'");

        return (spec[..dot], spec[(dot + 1)..]);
    }

    // ── Symbol resolution ─────────────────────────────────────────────────────

    private static async Task<(IMethodSymbol methodSymbol, DocumentId docId)>
        FindMethodAndDocumentAsync(Solution sln, string typeName, string methodName)
    {
        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var typeSymbol in compilation.GetSymbolsWithName(typeName, SymbolFilter.Type)
                         .OfType<INamedTypeSymbol>())
            {
                var method = typeSymbol.GetMembers(methodName)
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(m => !m.IsImplicitlyDeclared);

                if (method == null) continue;

                var docId = SymbolDocumentLocator.FindDocumentId(sln, method)
                    ?? throw new InvalidOperationException(
                        $"Cannot locate source document for '{typeName}.{methodName}'.");

                return (method, docId);
            }
        }

        throw new InvalidOperationException(
            $"Method '{typeName}.{methodName}' not found in solution.");
    }

    // ── Safety check ──────────────────────────────────────────────────────────

    private static async Task<string?> CheckSafetyAsync(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.IsStatic)
            return "method is already static.";

        if (methodSymbol.IsVirtual)
            return "method is virtual — removing virtual dispatch would change semantics.";

        if (methodSymbol.IsOverride)
            return "method is an override — it participates in polymorphism.";

        if (methodSymbol.IsAbstract)
            return "method is abstract.";

        var syntaxRef = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef == null)
            return "method has no source declaration.";

        var methodSyntax = (MethodDeclarationSyntax)await syntaxRef.GetSyntaxAsync();
        var body = (SyntaxNode?)methodSyntax.Body ?? methodSyntax.ExpressionBody;
        if (body == null)
            return "method has no body.";

        return DetectInstanceUsage(body, methodSymbol.ContainingType);
    }

    private static string? DetectInstanceUsage(SyntaxNode body, INamedTypeSymbol containingType)
    {
        // Explicit `this` keyword
        var thisExpr = body.DescendantNodes()
            .OfType<ThisExpressionSyntax>()
            .FirstOrDefault();

        if (thisExpr != null)
        {
            var lineNumber = thisExpr.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            return $"uses 'this' at line {lineNumber}.";
        }

        // Instance member access — simple identifier that resolves to instance field/property/event
        // We detect by looking for IdentifierNameSyntax nodes that aren't already qualified with
        // the type name and whose parent is a MemberAccessExpression targeting 'this' (already caught),
        // or are bare identifiers referencing instance fields/properties.
        // We flag MemberAccessExpression where the left side is not `Type.` or a static expression.
        // A conservative heuristic: any bare identifier inside the body that shares a name with
        // an instance member of the containing type is flagged.
        var instanceMemberNames = containingType.GetMembers()
            .Where(m => !m.IsStatic &&
                        m.Kind is SymbolKind.Field or SymbolKind.Property or SymbolKind.Event)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        if (instanceMemberNames.Count == 0)
            return null;

        foreach (var id in body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (!instanceMemberNames.Contains(id.Identifier.Text))
                continue;

            // Skip if this identifier is the right-hand part of a type-qualified member access
            // e.g. SomeType.FieldName — we only block bare access
            if (id.Parent is MemberAccessExpressionSyntax mae && mae.Name == id)
            {
                // Left side is an explicit object — could be 'this' (already caught) or another
                // instance. Either way, if it's not a type name, it's instance access.
                // For our purposes: 'this.X' → 'this' already caught above.
                // We skip this path to avoid false positives from SomeOtherObject.FieldName.
                continue;
            }

            var lineNumber = id.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            return $"accesses instance member '{id.Identifier.Text}' at line {lineNumber}.";
        }

        return null;
    }

    // ── Add static modifier ───────────────────────────────────────────────────

    private static async Task<Solution> AddStaticModifierAsync(
        Solution sln, IMethodSymbol methodSymbol, DocumentId docId)
    {
        var syntaxRef = methodSymbol.DeclaringSyntaxReferences.First();
        var methodSyntax = (MethodDeclarationSyntax)await syntaxRef.GetSyntaxAsync();

        var doc = sln.GetDocument(docId)!;
        var root = (await doc.GetSyntaxRootAsync())!;

        var oldNode = root.FindNode(syntaxRef.Span) as MethodDeclarationSyntax
            ?? throw new InvalidOperationException("Cannot locate method syntax node in document.");

        var staticToken = SyntaxFactory.Token(SyntaxKind.StaticKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);

        // Insert static after access modifiers (public/private/protected/internal) but before others
        var modifiers = oldNode.Modifiers;
        int insertIndex = 0;
        for (int i = 0; i < modifiers.Count; i++)
        {
            var kind = modifiers[i].Kind();
            if (kind is SyntaxKind.PublicKeyword or SyntaxKind.PrivateKeyword
                     or SyntaxKind.ProtectedKeyword or SyntaxKind.InternalKeyword)
            {
                insertIndex = i + 1;
            }
        }

        var newModifiers = modifiers.Insert(insertIndex, staticToken);
        var newMethod = oldNode.WithModifiers(newModifiers);
        root = root.ReplaceNode(oldNode, newMethod);

        return sln.WithDocumentSyntaxRoot(docId, root);
    }

    // ── Call-site rewriting ───────────────────────────────────────────────────

    private static async Task<Solution> RewriteCallSitesAsync(
        Solution sln, IMethodSymbol methodSymbol, string typeName, Solution symbolSolution)
    {
        // Find references using the original (pre-modification) solution's symbol
        var references = await SymbolFinder.FindReferencesAsync(methodSymbol, symbolSolution);

        var locationsByDocument = references
            .SelectMany(r => r.Locations)
            .GroupBy(l => l.Document.FilePath, StringComparer.OrdinalIgnoreCase);

        foreach (var group in locationsByDocument)
        {
            var filePath = group.Key;
            if (filePath == null) continue;

            // Find the document in the (possibly already modified) solution
            var doc = sln.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (doc == null) continue;

            var root = (await doc.GetSyntaxRootAsync())!;

            // Collect all spans from the original solution for this document
            var spans = group
                .Select(l => l.Location.SourceSpan)
                .OrderByDescending(s => s.Start)
                .ToList();

            bool changed = false;
            foreach (var span in spans)
            {
                var node = root.FindNode(span);

                // Navigate up to the invocation expression's member access
                // node is typically an IdentifierNameSyntax for the method name
                if (node.Parent is MemberAccessExpressionSyntax mae &&
                    mae.Parent is InvocationExpressionSyntax)
                {
                    // mae.Expression is the receiver (instance or type)
                    var receiver = mae.Expression;

                    // If receiver is already the type name, skip (already static call)
                    if (receiver is IdentifierNameSyntax id &&
                        id.Identifier.Text == typeName)
                        continue;

                    // Replace receiver with the type name
                    var typeAccess = SyntaxFactory.IdentifierName(typeName)
                        .WithLeadingTrivia(receiver.GetLeadingTrivia())
                        .WithTrailingTrivia(receiver.GetTrailingTrivia());

                    var newMae = mae.WithExpression(typeAccess);
                    root = root.ReplaceNode(mae, newMae);
                    changed = true;
                }
            }

            if (changed)
            {
                sln = sln.WithDocumentSyntaxRoot(doc.Id, root);
            }
        }

        return sln;
    }
}
