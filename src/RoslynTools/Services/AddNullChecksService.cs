using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// A class for managing null checks in the solution.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class AddNullChecksService : IRtService
{
    private readonly IDocumentLocatorService _documents;
    private readonly ISymbolResolutionService _symbols;

    public AddNullChecksService(
        ISymbolResolutionService symbols,
        IDocumentLocatorService documents)
    {
        _symbols = symbols;
        _documents = documents;
    }

    /// <summary>
    /// An asynchronous method to run null checks.
    /// </summary>
    internal sealed record Preparation(Solution Solution, bool Changed, string? Message = null);

    internal async Task<Preparation> PrepareAsync(Solution sln, string typeName, string methodName)
    {
        var methodSymbol = await _symbols.FindMethodInTypeAsync(sln, typeName, methodName);
        if (methodSymbol == null)
            throw new InvalidOperationException($"Method '{methodName}' not found in type '{typeName}'.");

        var syntaxRef = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef == null)
            throw new InvalidOperationException($"No source declaration found for '{methodName}'.");

        var methodSyntax = (MethodDeclarationSyntax)await syntaxRef.GetSyntaxAsync();
        if (methodSyntax.Body == null)
        {
            return new Preparation(sln, false, "Method has no block body — skipped.");
        }

        var eligibleParams = methodSymbol.Parameters
            .Where(p => !p.Type.IsValueType)
            .Where(p => p.NullableAnnotation != NullableAnnotation.Annotated)
            .Where(p => !HasExistingGuard(methodSyntax.Body, p.Name))
            .ToList();

        if (eligibleParams.Count == 0)
        {
            return new Preparation(sln, false, "No null checks needed.");
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

        var docId = _documents.FindDocumentId(sln, methodSymbol);
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
        return new Preparation(sln, true);
    }

    /// <summary>
    /// A method to check for existing guards in a block syntax.
    /// </summary>
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
