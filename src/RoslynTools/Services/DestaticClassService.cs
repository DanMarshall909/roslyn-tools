using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance service used by the static-to-instance migration dogfood path.")]
internal sealed class DestaticClassService
{
    internal async Task RunAsync(Solution solution, string typeName, bool dryRun)
    {
        var originalSolution = solution;
        var symbol = await SymbolLocator.FindTypeByFqnAsync(solution, typeName);

        if (symbol is not INamedTypeSymbol typeSymbol)
            throw new InvalidOperationException($"'{typeName}' is not a type.");

        if (!typeSymbol.IsStatic)
            throw new InvalidOperationException($"'{typeName}' is not a static class.");

        var syntaxRef = typeSymbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef is null)
            throw new InvalidOperationException($"No source declaration found for '{typeName}'.");

        var classDeclaration = await syntaxRef.GetSyntaxAsync() as ClassDeclarationSyntax
            ?? throw new InvalidOperationException($"Source declaration for '{typeName}' is not a class.");

        var declaringDocumentId = SymbolDocumentLocator.FindDocumentId(solution, typeSymbol)
            ?? throw new InvalidOperationException($"Cannot locate source document for '{typeName}'.");

        solution = await RewriteCallSitesAsync(solution, typeSymbol);
        solution = await RewriteDeclarationAsync(solution, declaringDocumentId, classDeclaration);

        RtLog.Info("Migrated static class '{TypeName}' to instance class.", typeSymbol.ToDisplayString());
        await SolutionWriter.ApplyChangesAsync(originalSolution, solution, dryRun);
    }

    private static async Task<Solution> RewriteDeclarationAsync(
        Solution solution,
        DocumentId documentId,
        ClassDeclarationSyntax originalClass)
    {
        var document = solution.GetDocument(documentId)
            ?? throw new InvalidOperationException("Cannot locate class document.");
        var root = await document.GetSyntaxRootAsync()
            ?? throw new InvalidOperationException("Cannot read class syntax root.");

        var currentClass = root.FindNode(originalClass.Span) as ClassDeclarationSyntax
            ?? throw new InvalidOperationException("Cannot locate class syntax node.");

        var updatedClass = currentClass.ReplaceNodes(
            currentClass.Members.OfType<BaseMethodDeclarationSyntax>()
                .Cast<SyntaxNode>()
                .Concat(currentClass.Members.OfType<PropertyDeclarationSyntax>())
                .Concat(currentClass.Members.OfType<FieldDeclarationSyntax>()),
            (_, rewritten) => RemoveMemberStaticModifier(rewritten));

        updatedClass = updatedClass
            .WithModifiers(RemoveStaticModifier(updatedClass.Modifiers))
            .AddAttributeLists(SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(SyntaxFactory.ParseName("SuppressMessage"))
                        .WithArgumentList(SyntaxFactory.ParseAttributeArgumentList(
                            "(\"Performance\", \"CA1822:Mark members as static\", Justification = \"Instance class created by rt destatic-class migration.\")")))));

        var newRoot = root.ReplaceNode(currentClass, updatedClass);
        if (newRoot is CompilationUnitSyntax compilationUnit)
            newRoot = RoslynSyntax.EnsureUsing(compilationUnit, "System.Diagnostics.CodeAnalysis");

        return solution.WithDocumentSyntaxRoot(documentId, newRoot);
    }

    private static async Task<Solution> RewriteCallSitesAsync(Solution solution, INamedTypeSymbol typeSymbol)
    {
        foreach (var project in solution.Projects.ToArray())
        {
            foreach (var document in project.Documents.ToArray())
            {
                var root = await document.GetSyntaxRootAsync();
                var semanticModel = await document.GetSemanticModelAsync();
                if (root is null || semanticModel is null)
                    continue;

                var rewriter = new StaticTypeCallRewriter(semanticModel, typeSymbol);
                var newRoot = rewriter.Visit(root);
                if (!rewriter.Changed || newRoot is null)
                    continue;

                solution = solution.WithDocumentSyntaxRoot(document.Id, newRoot);
            }
        }

        return solution;
    }

    private static SyntaxTokenList RemoveStaticModifier(SyntaxTokenList modifiers)
    {
        return SyntaxFactory.TokenList(modifiers.Where(m => !m.IsKind(SyntaxKind.StaticKeyword)));
    }

    private static SyntaxNode RemoveMemberStaticModifier(SyntaxNode node)
    {
        return node switch
        {
            BaseMethodDeclarationSyntax method => method.WithModifiers(RemoveStaticModifier(method.Modifiers)),
            PropertyDeclarationSyntax property => property.WithModifiers(RemoveStaticModifier(property.Modifiers)),
            FieldDeclarationSyntax field => field.WithModifiers(RemoveStaticModifier(field.Modifiers)),
            _ => node,
        };
    }

    private sealed class StaticTypeCallRewriter(SemanticModel semanticModel, INamedTypeSymbol typeSymbol) : CSharpSyntaxRewriter
    {
        private readonly string _targetTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        public bool Changed { get; private set; }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
            var expressionSymbol = semanticModel.GetSymbolInfo(node.Expression).Symbol;
            if (expressionSymbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) != _targetTypeName)
                return visited;

            var objectCreation = SyntaxFactory.ParseExpression($"new {typeSymbol.Name}()")
                .WithTriviaFrom(visited.Expression);

            Changed = true;
            return visited.WithExpression(objectCreation);
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
            if (node.Expression is not SimpleNameSyntax simpleName)
                return visited;

            var symbol = semanticModel.GetSymbolInfo(simpleName).Symbol;
            if (symbol?.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) != _targetTypeName)
                return visited;

            var enclosingType = semanticModel.GetEnclosingSymbol(node.SpanStart)?.ContainingType;
            if (enclosingType is not null
                && enclosingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == _targetTypeName)
            {
                return visited;
            }

            var receiver = SyntaxFactory.ParseExpression($"new {typeSymbol.Name}()");
            Changed = true;
            return visited.WithExpression(SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                receiver,
                simpleName));
        }
    }
}
