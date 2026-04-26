using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal static class MakeAsyncService
{
    internal static async Task RunAsync(Solution sln, string typeName, string methodName, bool noRename, bool dryRun)
    {
        var originalSolution = sln;

        var methodSymbol = await SymbolLocator.FindMethodInTypeAsync(sln, typeName, methodName);
        if (methodSymbol == null)
            throw new InvalidOperationException($"Method '{methodName}' not found in type '{typeName}'.");

        if (methodSymbol.IsAsync)
        {
            Console.WriteLine("Method is already async.");
            return;
        }

        var newMethodName = noRename ? methodName : methodName + "Async";

        var targetDocId = SymbolDocumentLocator.FindDocumentId(sln, methodSymbol);
        if (targetDocId == null)
            throw new InvalidOperationException("Cannot locate source document for the target method.");

        var refs = await SymbolFinder.FindReferencesAsync(methodSymbol, sln);
        var declarationSpan = methodSymbol.Locations.FirstOrDefault(l => l.IsInSource)?.SourceSpan;
        var callSitesByDoc = refs
            .SelectMany(r => r.Locations)
            .Where(loc =>
            {
                if (loc.Document.Id == targetDocId && loc.Location.SourceSpan == declarationSpan)
                    return false;
                return true;
            })
            .GroupBy(loc => loc.Document.Id)
            .ToDictionary(g => g.Key, g => g.Select(l => l.Location.SourceSpan).ToList());

        var allDocIds = new HashSet<DocumentId>(callSitesByDoc.Keys) { targetDocId };

        foreach (var docId in allDocIds)
        {
            var doc = sln.GetDocument(docId)!;
            var root = (await doc.GetSyntaxRootAsync())!;

            callSitesByDoc.TryGetValue(docId, out var callSpans);

            root = TransformDocument(
                root,
                isTargetDoc: docId == targetDocId,
                methodSymbol,
                newMethodName,
                methodName,
                callSpans ?? new List<TextSpan>());

            if (root is CompilationUnitSyntax cu)
                root = RoslynSyntax.EnsureUsing(cu, "System.Threading.Tasks");

            sln = sln.WithDocumentSyntaxRoot(docId, root);
        }

        await SolutionWriter.ApplyChangesAsync(originalSolution, sln, dryRun);
    }

    internal static SyntaxNode TransformDocument(
        SyntaxNode root,
        bool isTargetDoc,
        IMethodSymbol methodSymbol,
        string newMethodName,
        string oldMethodName,
        List<TextSpan> callSpans)
    {
        MethodDeclarationSyntax? targetDecl = null;
        if (isTargetDoc)
        {
            var declLocation = methodSymbol.Locations.FirstOrDefault(l => l.IsInSource);
            if (declLocation != null)
            {
                var node = root.FindNode(declLocation.SourceSpan);
                targetDecl = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            }
        }

        var callInvocations = new List<InvocationExpressionSyntax>();
        foreach (var span in callSpans)
        {
            var refNode = root.FindNode(span);
            var invocation = refNode.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            if (invocation != null && !(invocation.Parent is AwaitExpressionSyntax))
                callInvocations.Add(invocation);
        }

        var methodsToPromote = callInvocations
            .Select(inv => inv.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault())
            .Where(m => m != null && !m!.Modifiers.Any(SyntaxKind.AsyncKeyword))
            .Select(m => m!)
            .Distinct(ReferenceEqualityComparer.Instance as IEqualityComparer<MethodDeclarationSyntax>)
            .ToList();

        var nodesToReplace = new HashSet<SyntaxNode>(ReferenceEqualityComparer.Instance);
        if (targetDecl != null) nodesToReplace.Add(targetDecl);
        foreach (var inv in callInvocations) nodesToReplace.Add(inv);
        foreach (var m in methodsToPromote) nodesToReplace.Add(m);

        if (nodesToReplace.Count == 0) return root;

        return root.ReplaceNodes(nodesToReplace, (original, rewritten) =>
        {
            if (targetDecl != null && ReferenceEquals(original, targetDecl))
                return TransformTargetDeclaration((MethodDeclarationSyntax)rewritten, newMethodName);

            if (original is InvocationExpressionSyntax invOrig && callInvocations.Any(i => ReferenceEquals(i, invOrig)))
            {
                var inv = (InvocationExpressionSyntax)rewritten;
                if (newMethodName != oldMethodName)
                    inv = RenameInvocationTarget(inv, oldMethodName, newMethodName);

                return SyntaxFactory.AwaitExpression(
                    SyntaxFactory.Token(SyntaxKind.AwaitKeyword)
                        .WithLeadingTrivia(inv.GetLeadingTrivia())
                        .WithTrailingTrivia(SyntaxFactory.Space),
                    inv.WithoutLeadingTrivia());
            }

            if (original is MethodDeclarationSyntax mOrig && methodsToPromote.Any(m => ReferenceEquals(m, mOrig)))
                return PromoteMethodToAsync((MethodDeclarationSyntax)rewritten);

            return rewritten;
        });
    }

    private static MethodDeclarationSyntax TransformTargetDeclaration(MethodDeclarationSyntax method, string newMethodName)
    {
        method = method.AddModifiers(
            SyntaxFactory.Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(SyntaxFactory.Space));

        var returnTypeText = method.ReturnType.ToString().Trim();
        TypeSyntax newReturnType = returnTypeText == "void"
            ? SyntaxFactory.ParseTypeName("Task").WithTriviaFrom(method.ReturnType)
            : SyntaxFactory.ParseTypeName($"Task<{returnTypeText}>").WithTriviaFrom(method.ReturnType);
        method = method.WithReturnType(newReturnType);

        method = method.WithIdentifier(
            SyntaxFactory.Identifier(newMethodName).WithTriviaFrom(method.Identifier));

        return method;
    }

    private static InvocationExpressionSyntax RenameInvocationTarget(
        InvocationExpressionSyntax inv, string oldName, string newName)
    {
        var identifiers = inv.Expression.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Where(id => id.Identifier.Text == oldName)
            .ToList();

        if (identifiers.Count == 0) return inv;

        var target = identifiers.Last();
        var renamed = target.WithIdentifier(
            SyntaxFactory.Identifier(newName).WithTriviaFrom(target.Identifier));
        return inv.ReplaceNode(target, renamed);
    }

    private static MethodDeclarationSyntax PromoteMethodToAsync(MethodDeclarationSyntax method)
    {
        method = method.AddModifiers(
            SyntaxFactory.Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(SyntaxFactory.Space));

        var returnTypeText = method.ReturnType.ToString().Trim();
        if (returnTypeText == "void")
        {
            method = method.WithReturnType(
                SyntaxFactory.ParseTypeName("Task").WithTriviaFrom(method.ReturnType));
        }
        else if (!returnTypeText.StartsWith("Task", StringComparison.Ordinal))
        {
            method = method.WithReturnType(
                SyntaxFactory.ParseTypeName($"Task<{returnTypeText}>").WithTriviaFrom(method.ReturnType));
        }

        return method;
    }
}
