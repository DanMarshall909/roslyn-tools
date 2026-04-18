using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynTools.Core;

/// <summary>
/// Core logic for the <c>transform-parameters</c> command.
/// Generates a new <c>record ParamObj(...)</c> in the same file as the target method,
/// rewrites the method signature to accept one <c>ParamObj args</c> parameter,
/// and updates every call site from <c>Foo(a, b, c)</c> to <c>Foo(new ParamObj(a, b, c))</c>.
/// </summary>
internal static class TransformParametersCore
{
    /// <summary>
    /// Entry point called by <see cref="RoslynTools.Commands.TransformParametersCommand"/>.
    /// </summary>
    /// <param name="sln">Loaded solution.</param>
    /// <param name="methodSpec">Dot-separated <c>TypeName.MethodName</c>.</param>
    /// <param name="newTypeName">Name of the record to generate.</param>
    /// <param name="dryRun">When true, print diff without writing files.</param>
    internal static async Task RunAsync(
        Solution sln,
        string methodSpec,
        string newTypeName,
        bool dryRun)
    {
        var originalSln = sln;

        var (methodSymbol, doc) = await FindMethodAsync(sln, methodSpec);

        var unsafeParams = CollectUnsafeParameters(methodSymbol);
        if (unsafeParams.Count > 0)
        {
            var names = string.Join(", ", unsafeParams);
            throw new InvalidOperationException(
                $"Cannot transform: parameter(s) '{names}' use out/ref/in modifiers which are not supported. " +
                "Remove or exclude those parameters before running this command.");
        }

        if (methodSymbol.Parameters.Length == 0)
            throw new InvalidOperationException(
                $"Method '{methodSpec}' has no parameters to transform.");

        // Rewrite call sites FIRST while spans are still valid against the original solution.
        sln = await RewriteCallSitesAsync(sln, methodSymbol, newTypeName);

        // Insert record declaration into the method's file.
        // doc still valid — call-site rewrite only touches call-site documents.
        sln = await InsertRecordDeclarationAsync(sln, doc, methodSymbol, newTypeName);

        // Re-resolve method symbol after syntax tree was modified to get fresh syntax reference.
        (methodSymbol, doc) = await FindMethodAsync(sln, methodSpec);
        sln = await RewriteMethodSignatureAsync(sln, doc, methodSymbol, newTypeName);

        await SolutionWriter.ApplyChangesAsync(originalSln, sln, dryRun);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Symbol resolution
    // ──────────────────────────────────────────────────────────────────────────

    private static async Task<(IMethodSymbol Symbol, Document Doc)>
        FindMethodAsync(Solution sln, string methodSpec)
    {
        var dotIdx = methodSpec.LastIndexOf('.');
        if (dotIdx <= 0)
            throw new InvalidOperationException(
                $"Method spec must be 'TypeName.MethodName', got: '{methodSpec}'");

        var typeName = methodSpec[..dotIdx];
        var methodName = methodSpec[(dotIdx + 1)..];

        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var typeSymbol in compilation.GetSymbolsWithName(typeName, SymbolFilter.Type)
                         .OfType<INamedTypeSymbol>())
            {
                var method = typeSymbol.GetMembers(methodName)
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(m => m.MethodKind == MethodKind.Ordinary);

                if (method == null) continue;

                var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
                if (syntaxRef == null) continue;

                var doc = sln.GetDocument(syntaxRef.SyntaxTree)
                    ?? throw new InvalidOperationException(
                        $"Cannot find document for method '{methodSpec}'.");

                return (method, doc);
            }
        }

        throw new InvalidOperationException(
            $"Method '{methodSpec}' not found in solution.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Validation
    // ──────────────────────────────────────────────────────────────────────────

    private static List<string> CollectUnsafeParameters(IMethodSymbol method)
    {
        return method.Parameters
            .Where(p => p.RefKind != RefKind.None)
            .Select(p => p.Name)
            .ToList();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Step 1: Insert record declaration into the same file as the method
    // ──────────────────────────────────────────────────────────────────────────

    private static async Task<Solution> InsertRecordDeclarationAsync(
        Solution sln,
        Document doc,
        IMethodSymbol method,
        string newTypeName)
    {
        var root = await doc.GetSyntaxRootAsync()
            ?? throw new InvalidOperationException("Cannot get syntax root.");

        var methodSyntax = (MethodDeclarationSyntax)await method.DeclaringSyntaxReferences[0].GetSyntaxAsync();
        var containingType = methodSyntax.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()
            ?? throw new InvalidOperationException("Method is not inside a type declaration.");

        var recordText = BuildRecordText(method.Parameters, newTypeName);
        var recordDecl = (RecordDeclarationSyntax)(SyntaxFactory.ParseMemberDeclaration(recordText)
            ?? throw new InvalidOperationException("Failed to parse generated record declaration."));

        // Place the record just before the containing type's closing brace, as a sibling
        // at the namespace/file level — after the containing type
        var newRoot = InsertAfterType(root, containingType, recordDecl);
        return sln.WithDocumentSyntaxRoot(doc.Id, newRoot);
    }

    private static string BuildRecordText(
        IReadOnlyList<IParameterSymbol> parameters,
        string newTypeName)
    {
        var paramParts = parameters.Select(p =>
        {
            var typeName = p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var defaultPart = p.HasExplicitDefaultValue
                ? " = " + FormatDefaultValue(p)
                : string.Empty;
            return $"{typeName} {Capitalize(p.Name)}{defaultPart}";
        });

        return $"internal sealed record {newTypeName}({string.Join(", ", paramParts)});";
    }

    private static string FormatDefaultValue(IParameterSymbol param)
    {
        if (param.ExplicitDefaultValue == null)
            return "null";
        if (param.ExplicitDefaultValue is string s)
            return $"\"{s}\"";
        if (param.ExplicitDefaultValue is bool b)
            return b ? "true" : "false";
        if (param.ExplicitDefaultValue is char c)
            return $"'{c}'";
        return param.ExplicitDefaultValue.ToString() ?? "default";
    }

    private static SyntaxNode InsertAfterType(
        SyntaxNode root,
        TypeDeclarationSyntax containingType,
        RecordDeclarationSyntax recordDecl)
    {
        // Find the parent — could be a namespace or the compilation unit
        var parent = containingType.Parent;

        if (parent is BaseNamespaceDeclarationSyntax nsDecl)
        {
            var members = nsDecl.Members.ToList();
            int idx = members.IndexOf(containingType);
            if (idx < 0)
                throw new InvalidOperationException("Cannot locate containing type in namespace members.");

            var formattedRecord = recordDecl
                .WithLeadingTrivia(SyntaxFactory.LineFeed, SyntaxFactory.Whitespace(MemberIndent(containingType)));

            members.Insert(idx + 1, formattedRecord);
            var newNs = nsDecl.WithMembers(SyntaxFactory.List(members));
            return root.ReplaceNode(nsDecl, newNs);
        }

        if (parent is CompilationUnitSyntax compilationUnit)
        {
            var members = compilationUnit.Members.ToList();
            int idx = members.IndexOf(containingType);
            if (idx < 0)
                throw new InvalidOperationException("Cannot locate containing type in compilation unit.");

            var formattedRecord = recordDecl
                .WithLeadingTrivia(SyntaxFactory.LineFeed, SyntaxFactory.Whitespace(MemberIndent(containingType)));

            members.Insert(idx + 1, formattedRecord);
            var newCu = compilationUnit.WithMembers(SyntaxFactory.List(members));
            return root.ReplaceNode(compilationUnit, newCu);
        }

        throw new InvalidOperationException(
            $"Unexpected containing scope type: {parent?.GetType().Name ?? "null"}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Step 2: Rewrite method signature
    // ──────────────────────────────────────────────────────────────────────────

    private static async Task<Solution> RewriteMethodSignatureAsync(
        Solution sln,
        Document doc,
        IMethodSymbol method,
        string newTypeName)
    {
        var root = await doc.GetSyntaxRootAsync()
            ?? throw new InvalidOperationException("Cannot get syntax root.");

        var methodSyntax = (MethodDeclarationSyntax)await method.DeclaringSyntaxReferences[0].GetSyntaxAsync();

        // Find the node in the (potentially updated) root by location
        var targetNode = root.FindNode(methodSyntax.Span) as MethodDeclarationSyntax
            ?? throw new InvalidOperationException("Cannot locate method syntax in updated document.");

        var newParam = SyntaxFactory.Parameter(
                SyntaxFactory.Identifier("args"))
            .WithType(SyntaxFactory.ParseTypeName(newTypeName + " "));

        var newParamList = SyntaxFactory.ParameterList(
            SyntaxFactory.SingletonSeparatedList(newParam));

        var newMethod = targetNode.WithParameterList(newParamList);
        var newRoot = root.ReplaceNode(targetNode, newMethod);
        return sln.WithDocumentSyntaxRoot(doc.Id, newRoot);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Step 3: Rewrite call sites
    // ──────────────────────────────────────────────────────────────────────────

    private static async Task<Solution> RewriteCallSitesAsync(
        Solution sln,
        IMethodSymbol method,
        string newTypeName)
    {
        var references = await SymbolFinder.FindReferencesAsync(method, sln);

        // Group rewrite tasks by document to batch changes per file
        var locationsByDoc = references
            .SelectMany(r => r.Locations)
            .Where(loc => loc.Location.IsInSource)
            .GroupBy(loc => sln.GetDocument(loc.Location.SourceTree!));

        foreach (var group in locationsByDoc)
        {
            var doc = group.Key;
            if (doc == null) continue;

            var root = await doc.GetSyntaxRootAsync();
            if (root == null) continue;

            // Collect all invocation nodes that match our reference locations
            var invocations = group
                .Select(loc =>
                {
                    var node = root.FindNode(loc.Location.SourceSpan);
                    // Walk up to find the InvocationExpressionSyntax
                    return node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
                })
                .Where(inv => inv != null)
                .Cast<InvocationExpressionSyntax>()
                .ToList();

            // Replace all in one pass via ReplaceNodes to avoid stale span issues
            var newRoot = root.ReplaceNodes(
                invocations,
                (original, _) => RewriteInvocation(original, newTypeName));

            sln = sln.WithDocumentSyntaxRoot(doc.Id, newRoot);
        }

        return sln;
    }

    private static InvocationExpressionSyntax RewriteInvocation(
        InvocationExpressionSyntax original,
        string newTypeName)
    {
        // Build: new ParamObj(arg1, arg2, arg3)
        var args = original.ArgumentList.Arguments;
        var newObjectCreation = SyntaxFactory.ObjectCreationExpression(
                SyntaxFactory.IdentifierName(newTypeName))
            .WithArgumentList(SyntaxFactory.ArgumentList(args));

        var newArgs = SyntaxFactory.ArgumentList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Argument(newObjectCreation)));

        return original.WithArgumentList(newArgs);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static string Capitalize(string name)
        => name.Length == 0 ? name : char.ToUpper(name[0]) + name[1..];

    private static string MemberIndent(TypeDeclarationSyntax type)
    {
        var ws = type.GetLeadingTrivia()
            .LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
        return ws == default ? string.Empty : ws.ToString();
    }
}
