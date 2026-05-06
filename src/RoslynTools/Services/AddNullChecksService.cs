using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance class created by rt destatic-class migration.")]
internal class AddNullChecksService : IRtService
{
    private readonly ISolutionChangeWriter _solutionChangeWriter;

    public AddNullChecksService()
        : this(new SolutionChangeWriter())
    {
    }

    public AddNullChecksService(ISolutionChangeWriter solutionChangeWriter)
    {
        _solutionChangeWriter = solutionChangeWriter;
    }
    internal async Task RunAsync(Solution sln, string typeName, string methodName, bool dryRun)
    {
        var originalSolution = sln;

        var methodSymbol = await SymbolLocator.FindMethodInTypeAsync(sln, typeName, methodName);
        if (methodSymbol == null)
            throw new InvalidOperationException($"Method '{methodName}' not found in type '{typeName}'.");

        var syntaxRef = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef == null)
            throw new InvalidOperationException($"No source declaration found for '{methodName}'.");

        var methodSyntax = (MethodDeclarationSyntax)await syntaxRef.GetSyntaxAsync();
        if (methodSyntax.Body == null)
        {
            RtConsole.WriteLine(RtOutputContext.Result, "Method has no block body — skipped.");
            return;
        }

        var eligibleParams = methodSymbol.Parameters
            .Where(p => !p.Type.IsValueType)
            .Where(p => p.NullableAnnotation != NullableAnnotation.Annotated)
            .Where(p => !HasExistingGuard(methodSyntax.Body, p.Name))
            .ToList();

        if (eligibleParams.Count == 0)
        {
            RtConsole.WriteLine(RtOutputContext.Result, "No null checks needed.");
            return;
        }

        var newStatements = eligibleParams
            .Select(p => SyntaxFactory.ParseStatement($"ArgumentNullException.ThrowIfNull({p.Name});\n")
                .WithLeadingTrivia(methodSyntax.Body.Statements.Count > 0
                    ? methodSyntax.Body.Statements[0].GetLeadingTrivia()
                    : SyntaxFactory.TriviaList(SyntaxFactory.Whitespace("            "))))
            .ToArray();

        var updatedStatements = new SyntaxList<StatementSyntax>(newStatements)
            .AddRange(methodSyntax.Body.Statements);

        var newBody = methodSyntax.Body.WithStatements(updatedStatements);
        var newMethod = methodSyntax.WithBody(newBody);

        var docId = SymbolDocumentLocator.FindDocumentId(sln, methodSymbol);
        if (docId == null)
            throw new InvalidOperationException("Cannot locate source document for the target method.");

        var doc = sln.GetDocument(docId)!;
        var root = (CompilationUnitSyntax)(await doc.GetSyntaxRootAsync())!;

        var oldMethodNode = root.FindNode(syntaxRef.Span) as MethodDeclarationSyntax;
        if (oldMethodNode == null)
            throw new InvalidOperationException("Cannot locate method syntax node in document.");

        root = root.ReplaceNode(oldMethodNode, newMethod);
        root = RoslynSyntax.EnsureUsing(root, "System");

        sln = sln.WithDocumentSyntaxRoot(docId, root);
        await _solutionChangeWriter.ApplyChangesAsync(originalSolution, sln, dryRun);
    }

    internal bool HasExistingGuard(BlockSyntax body, string paramName)
    {
        foreach (var stmt in body.Statements)
        {
            var text = stmt.ToString();
            if (text.Contains($"ThrowIfNull({paramName})", StringComparison.Ordinal))
                return true;
            if (text.Contains($"{paramName} != null", StringComparison.Ordinal) ||
                text.Contains($"null != {paramName}", StringComparison.Ordinal) ||
                text.Contains($"{paramName} is null", StringComparison.Ordinal) ||
                text.Contains($"{paramName} == null", StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
