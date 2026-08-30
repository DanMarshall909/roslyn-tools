using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// A class for managing XML documentation services.
/// </summary>
internal sealed class AddXmlDocService : IRtService
{
    internal sealed record XmlDocPreparation(
        Solution Solution,
        int Applied,
        IReadOnlyList<string> SkippedSymbols);

    private readonly ISymbolResolutionService _symbols;

    public AddXmlDocService(ISymbolResolutionService symbols)
    {
        _symbols = symbols;
    }

    internal async Task<XmlDocPreparation> PrepareAsync(
        Solution sln,
        IReadOnlyDictionary<string, string> docs,
        CancellationToken cancellationToken = default)
    {
        var updated = sln;
        var applied = 0;
        var skippedSymbols = new List<string>();

        foreach (var (fqn, summary) in docs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ISymbol? symbol;
            try
            {
                symbol = await _symbols.FindSymbolByFqnAsync(updated, fqn, null);
            }
            catch (InvalidOperationException)
            {
                symbol = null;
            }

            if (symbol is null)
            {
                skippedSymbols.Add(fqn);
                continue;
            }

            foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
            {
                var node = await syntaxRef.GetSyntaxAsync(cancellationToken);
                if (HasXmlDoc(node)) continue;

                var document = updated.GetDocument(node.SyntaxTree);
                if (document is null) continue;

                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root is null) continue;

                var newNode = InsertXmlDoc(node, summary);
                var newRoot = root.ReplaceNode(node, newNode);
                updated = updated.WithDocumentSyntaxRoot(document.Id, newRoot);
                applied++;
            }
        }

        return new XmlDocPreparation(updated, applied, skippedSymbols);
    }

    private static SyntaxNode InsertXmlDoc(SyntaxNode node, string summary)
    {
        var leading = node.GetLeadingTrivia();

        // Find the indentation immediately before the declaration.
        var indent = leading.Count > 0 && leading.Last().IsKind(SyntaxKind.WhitespaceTrivia)
            ? leading.Last().ToString()
            : string.Empty;

        var escaped = EscapeXml(summary);
        var nl = "\r\n";
        var docText = $"{indent}/// <summary>{nl}{indent}/// {escaped}{nl}{indent}/// </summary>{nl}";
        var docTrivia = SyntaxFactory.ParseLeadingTrivia(docText);

        // Insert doc comment before the final indentation trivia.
        var insertAt = leading.Count > 0 && leading.Last().IsKind(SyntaxKind.WhitespaceTrivia)
            ? leading.Count - 1
            : leading.Count;

        var newLeading = leading.InsertRange(insertAt, docTrivia);
        return node.WithLeadingTrivia(newLeading);
    }

    private static bool HasXmlDoc(SyntaxNode node) =>
        node.GetLeadingTrivia().Any(t =>
            t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
            t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

    private static string EscapeXml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
