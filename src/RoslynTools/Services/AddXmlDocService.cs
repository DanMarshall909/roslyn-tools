using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed class AddXmlDocService : IRtService
{
    private readonly ISolutionChangeWriter _writer;

    public AddXmlDocService() : this(new SolutionChangeWriter()) { }

    public AddXmlDocService(ISolutionChangeWriter writer) => _writer = writer;

    internal async Task RunAsync(Solution sln, IReadOnlyDictionary<string, string> docs, bool dryRun)
    {
        var updated = sln;
        var applied = 0;
        var skipped = 0;

        foreach (var (fqn, summary) in docs)
        {
            ISymbol? symbol;
            try { symbol = await SymbolLocator.FindSymbolByFqnAsync(updated, fqn, null); }
            catch { symbol = null; }

            if (symbol is null)
            {
                RtLog.Error($"Symbol not found, skipping: {fqn}");
                skipped++;
                continue;
            }

            foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
            {
                var node = await syntaxRef.GetSyntaxAsync();
                if (HasXmlDoc(node)) continue;

                var document = updated.GetDocument(node.SyntaxTree);
                if (document is null) continue;

                var root = await document.GetSyntaxRootAsync();
                if (root is null) continue;

                var newNode = InsertXmlDoc(node, summary);
                var newRoot = root.ReplaceNode(node, newNode);
                updated = updated.WithDocumentSyntaxRoot(document.Id, newRoot);
                applied++;
            }
        }

        RtLog.Info($"XML docs: {applied} applied, {skipped} symbols not found.");
        await _writer.ApplyChangesAsync(sln, updated, dryRun);
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
