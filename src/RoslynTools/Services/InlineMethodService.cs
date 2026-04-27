using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal static class InlineMethodService
{
    internal static async Task RunAsync(Solution sln, string typeName, string methodName, bool dryRun)
    {
        var originalSolution = sln;

        var methodSymbol = await SymbolLocator.FindMethodInTypeAsync(sln, typeName, methodName);
        if (methodSymbol == null)
        {
            RtLog.Error("Error: Method '{MethodName}' not found in type '{TypeName}'.", methodName, typeName);
            return;
        }

        var bodyExpression = GetInlinableBodyExpression(methodSymbol);
        if (bodyExpression == null)
        {
            RtLog.Error("Error: inline-method only supports expression-bodied and single-return block methods.");
            return;
        }

        var parameters = methodSymbol.Parameters;

        var refs = await SymbolFinder.FindReferencesAsync(methodSymbol, sln);
        var callSitesByDoc = refs
            .SelectMany(r => r.Locations)
            .GroupBy(loc => loc.Document.Id)
            .ToDictionary(g => g.Key, g => g.Select(l => l.Location.SourceSpan).ToList());

        var declDocId = SymbolDocumentLocator.FindDocumentId(sln, methodSymbol);
        if (declDocId == null)
        {
            RtLog.Error("Error: Cannot locate source document for '{MethodName}'.", methodName);
            return;
        }

        var allDocIds = new HashSet<DocumentId>(callSitesByDoc.Keys) { declDocId };

        foreach (var docId in allDocIds)
        {
            var doc = sln.GetDocument(docId)!;
            var root = (await doc.GetSyntaxRootAsync())!;

            callSitesByDoc.TryGetValue(docId, out var callSpans);
            var isTargetDoc = docId == declDocId;

            root = TransformDocument(
                root,
                isTargetDoc,
                methodSymbol,
                bodyExpression,
                parameters,
                callSpans ?? new List<TextSpan>());

            sln = sln.WithDocumentSyntaxRoot(docId, root);
        }

        await SolutionWriter.ApplyChangesAsync(originalSolution, sln, dryRun);
    }

    internal static SyntaxNode TransformDocument(
        SyntaxNode root,
        bool isTargetDoc,
        IMethodSymbol methodSymbol,
        ExpressionSyntax bodyExpression,
        System.Collections.Immutable.ImmutableArray<IParameterSymbol> parameters,
        List<TextSpan> callSpans)
    {
        var callInvocations = new List<InvocationExpressionSyntax>();
        foreach (var span in callSpans)
        {
            var refNode = root.FindNode(span);
            var invocation = refNode.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            if (invocation != null)
                callInvocations.Add(invocation);
        }

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

        if (callInvocations.Count == 0 && targetDecl == null)
            return root;

        var nodesToReplace = new HashSet<SyntaxNode>(ReferenceEqualityComparer.Instance);
        foreach (var inv in callInvocations) nodesToReplace.Add(inv);
        if (targetDecl != null) nodesToReplace.Add(targetDecl);

        var targetAnnotation = new SyntaxAnnotation("inline-method-target");

        var result = root.ReplaceNodes(nodesToReplace, (original, rewritten) =>
        {
            if (original is InvocationExpressionSyntax invOrig &&
                callInvocations.Any(i => ReferenceEquals(i, invOrig)))
            {
                var inv = (InvocationExpressionSyntax)rewritten;
                var args = inv.ArgumentList.Arguments;

                var substitution = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
                for (int i = 0; i < parameters.Length && i < args.Count; i++)
                    substitution[parameters[i].Name] = args[i].Expression;

                var inlined = SubstituteParameters(bodyExpression, substitution);
                return inlined.WithTriviaFrom(inv);
            }

            if (targetDecl != null && ReferenceEquals(original, targetDecl))
                return rewritten.WithAdditionalAnnotations(targetAnnotation);

            return rewritten;
        });

        if (targetDecl != null)
        {
            var declNode = result.GetAnnotatedNodes(targetAnnotation)
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault();

            if (declNode != null)
                result = result.RemoveNode(declNode, SyntaxRemoveOptions.KeepNoTrivia)!;
        }

        return result;
    }

    private static ExpressionSyntax SubstituteParameters(
        ExpressionSyntax expression,
        Dictionary<string, ExpressionSyntax> substitution)
    {
        if (substitution.Count == 0) return expression;
        return (ExpressionSyntax)new ParameterSubstitutor(substitution).Visit(expression)!;
    }

    private static ExpressionSyntax? GetInlinableBodyExpression(IMethodSymbol methodSymbol)
    {
        var location = methodSymbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location?.SourceTree == null) return null;

        var root = location.SourceTree.GetRoot();
        var node = root.FindNode(location.SourceSpan);
        var methodDecl = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (methodDecl == null) return null;

        if (methodDecl.ExpressionBody != null)
            return methodDecl.ExpressionBody.Expression;

        if (methodDecl.Body != null)
        {
            var statements = methodDecl.Body.Statements;
            if (statements.Count == 1 && statements[0] is ReturnStatementSyntax ret && ret.Expression != null)
                return ret.Expression;
        }

        return null;
    }

    private sealed class ParameterSubstitutor : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, ExpressionSyntax> _substitution;

        public ParameterSubstitutor(Dictionary<string, ExpressionSyntax> substitution)
        {
            _substitution = substitution;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (_substitution.TryGetValue(node.Identifier.Text, out var replacement))
            {
                var safe = NeedsParens(replacement)
                    ? SyntaxFactory.ParenthesizedExpression(replacement.WithoutTrivia())
                    : replacement;
                return safe.WithTriviaFrom(node);
            }
            return base.VisitIdentifierName(node);
        }

        private static bool NeedsParens(ExpressionSyntax expr) => expr is
            BinaryExpressionSyntax or
            ConditionalExpressionSyntax or
            AssignmentExpressionSyntax or
            PrefixUnaryExpressionSyntax or
            PostfixUnaryExpressionSyntax or
            CastExpressionSyntax or
            IsPatternExpressionSyntax or
            AwaitExpressionSyntax or
            LambdaExpressionSyntax or
            RangeExpressionSyntax;
    }
}
