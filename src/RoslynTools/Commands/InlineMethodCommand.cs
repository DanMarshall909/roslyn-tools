using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class InlineMethodCommand : IRtCommand
{
    private InlineMethodCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "inline-method",
        Mutates: true,
        JsonOutput: false,
        Category: "Mutation",
        Aliases: ["im"],
        WhenToUse: "Inlining a trivial helper method (expression-bodied or single-return block) at every call site and removing the method declaration. Best for one-liner wrappers that add no clarity — over-extracted private methods that obscure intent.",
        Examples:
        [
            "rt inline-method --type MathHelper --method Double --solution RoslynTools.slnx",
            "rt im --type OrderService --method GetTotal --dry-run --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
        ],
        KnownIssues:
        [
            "Method removal after inlining matches by identifier text only — removes the wrong method when the same name exists in a different class in the same file. Fix: scope to containing TypeDeclarationSyntax by type name and parameter count. See ai-improvements row #8.",
            "ParameterSubstitutor does not parenthesise substituted arguments — `Double(a+b)` inlines as `a+b*2` instead of `(a+b)*2`. Fix: wrap non-trivial argument expressions in ParenthesizedExpressionSyntax. See ai-improvements row #9.",
        ]
    );

    public static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Class containing the method to inline") { IsRequired = true };
        var methodOpt = new Option<string>("--method", "Method name to inline at all call sites") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("inline-method", "Inline a method by replacing all call sites with the method body");
        cmd.AddAlias("im");
        cmd.Add(typeOpt); cmd.Add(methodOpt); cmd.Add(solutionOpt); cmd.Add(dryRunOpt);

        cmd.SetHandler(async (type, method, solution, dryRun) =>
        {
            await ExecuteAsync(type, method, solution, dryRun);
        }, typeOpt, methodOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(string typeName, string methodName, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            var originalSolution = sln;

            // Find the target method symbol
            var methodSymbol = await FindMethodSymbolAsync(sln, typeName, methodName);
            if (methodSymbol == null)
            {
                Console.Error.WriteLine($"Error: Method '{methodName}' not found in type '{typeName}'.");
                return;
            }

            // Extract the inlinable body expression
            var bodyExpression = GetInlinableBodyExpression(methodSymbol);
            if (bodyExpression == null)
            {
                Console.Error.WriteLine("Error: inline-method only supports expression-bodied and single-return block methods.");
                return;
            }

            var parameters = methodSymbol.Parameters;

            // Collect all call-site reference spans from the original solution
            var refs = await SymbolFinder.FindReferencesAsync(methodSymbol, sln);
            var callSitesByDoc = refs
                .SelectMany(r => r.Locations)
                .GroupBy(loc => loc.Document.Id)
                .ToDictionary(g => g.Key, g => g.Select(l => l.Location.SourceSpan).ToList());

            // Find the document containing the method declaration
            var declDocId = SymbolDocumentLocator.FindDocumentId(sln, methodSymbol);
            if (declDocId == null)
            {
                Console.Error.WriteLine($"Error: Cannot locate source document for '{methodName}'.");
                return;
            }

            // Process all documents that have call sites, plus the declaration document
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
    }

    /// <summary>
    /// Transforms one document: replaces invocations with the inlined body and (if target doc) removes the method declaration.
    /// </summary>
    private static SyntaxNode TransformDocument(
        SyntaxNode root,
        bool isTargetDoc,
        IMethodSymbol methodSymbol,
        ExpressionSyntax bodyExpression,
        System.Collections.Immutable.ImmutableArray<IParameterSymbol> parameters,
        List<TextSpan> callSpans)
    {
        // Locate invocation nodes from the original span positions
        var callInvocations = new List<InvocationExpressionSyntax>();
        foreach (var span in callSpans)
        {
            var refNode = root.FindNode(span);
            var invocation = refNode.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            if (invocation != null)
                callInvocations.Add(invocation);
        }

        // Locate the method declaration in the target document
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

        // Build the set of nodes to replace in a single pass
        var nodesToReplace = new HashSet<SyntaxNode>(ReferenceEqualityComparer.Instance);
        foreach (var inv in callInvocations) nodesToReplace.Add(inv);
        if (targetDecl != null) nodesToReplace.Add(targetDecl);

        var result = root.ReplaceNodes(nodesToReplace, (original, rewritten) =>
        {
            // Replace invocation with inlined expression
            if (original is InvocationExpressionSyntax invOrig &&
                callInvocations.Any(i => ReferenceEquals(i, invOrig)))
            {
                var inv = (InvocationExpressionSyntax)rewritten;
                var args = inv.ArgumentList.Arguments;

                // Build substitution map: parameter name -> argument expression
                var substitution = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
                for (int i = 0; i < parameters.Length && i < args.Count; i++)
                    substitution[parameters[i].Name] = args[i].Expression;

                var inlined = SubstituteParameters(bodyExpression, substitution);

                // Preserve leading trivia from the original invocation
                return inlined.WithTriviaFrom(inv);
            }

            // Remove the method declaration
            if (targetDecl != null && ReferenceEquals(original, targetDecl))
            {
                // Return null is not valid in ReplaceNodes — we'll handle removal separately
                // We mark it by returning an empty placeholder that we strip after
                return rewritten; // handled via RemoveNode below
            }

            return rewritten;
        });

        // Remove the method declaration separately — ReplaceNodes cannot delete nodes.
        // After call-site replacements spans shift, so locate by identifier name.
        if (targetDecl != null)
        {
            var declNode = result
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == methodSymbol.Name);

            if (declNode != null)
                result = result.RemoveNode(declNode, SyntaxRemoveOptions.KeepNoTrivia)!;
        }

        return result;
    }

    /// <summary>
    /// Substitutes parameter references in the body expression with the corresponding argument expressions.
    /// </summary>
    private static ExpressionSyntax SubstituteParameters(
        ExpressionSyntax expression,
        Dictionary<string, ExpressionSyntax> substitution)
    {
        if (substitution.Count == 0) return expression;

        return (ExpressionSyntax)new ParameterSubstitutor(substitution).Visit(expression)!;
    }

    /// <summary>
    /// Extracts the inlinable body expression from a method symbol.
    /// Returns null if the method is not expression-bodied or block-bodied with exactly one return.
    /// </summary>
    private static ExpressionSyntax? GetInlinableBodyExpression(IMethodSymbol methodSymbol)
    {
        var location = methodSymbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location?.SourceTree == null) return null;

        var root = location.SourceTree.GetRoot();
        var node = root.FindNode(location.SourceSpan);
        var methodDecl = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (methodDecl == null) return null;

        // Expression-bodied: => expr
        if (methodDecl.ExpressionBody != null)
            return methodDecl.ExpressionBody.Expression;

        // Block body with exactly one return statement
        if (methodDecl.Body != null)
        {
            var statements = methodDecl.Body.Statements;
            if (statements.Count == 1 && statements[0] is ReturnStatementSyntax ret && ret.Expression != null)
                return ret.Expression;
        }

        return null;
    }

    private static async Task<IMethodSymbol?> FindMethodSymbolAsync(Solution sln, string typeName, string methodName)
    {
        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var symbol in compilation.GetSymbolsWithName(typeName, SymbolFilter.Type))
            {
                if (symbol is INamedTypeSymbol typeSymbol)
                {
                    var method = typeSymbol.GetMembers(methodName)
                        .OfType<IMethodSymbol>()
                        .FirstOrDefault();
                    if (method != null)
                        return method;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Rewrites IdentifierNameSyntax nodes whose names match parameter names with the corresponding argument expressions.
    /// </summary>
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
                // Strip trivia from the replacement and inherit from the identifier being replaced
                return replacement.WithTriviaFrom(node);
            }
            return base.VisitIdentifierName(node);
        }
    }
}
