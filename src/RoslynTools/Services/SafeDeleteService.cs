using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal static class SafeDeleteService
{
    internal static async Task RunAsync(Solution sln, string symbol, string? kind, bool dryRun)
    {
        var sym = await ResolveSymbolAsync(sln, symbol, kind);

        RtLog.Info("Checking references for '{Symbol}'...", sym.ToDisplayString());

        var referencedSymbols = await SymbolFinder.FindReferencesAsync(sym, sln);

        var trueRefs = new List<(string FilePath, int Line, string LineText)>();

        foreach (var referencedSymbol in referencedSymbols)
        {
            foreach (var loc in referencedSymbol.Locations)
            {
                var span = loc.Location.SourceSpan;
                var filePath = loc.Document.FilePath ?? string.Empty;

                var declSyntaxRef = sym.DeclaringSyntaxReferences.FirstOrDefault(r =>
                    string.Equals(r.SyntaxTree.FilePath, filePath, StringComparison.OrdinalIgnoreCase)
                    && r.Span.Contains(span));
                if (declSyntaxRef != null)
                    continue;

                var syntaxNode = loc.Location.SourceTree is not null
                    ? (await loc.Location.SourceTree.GetRootAsync())
                        .FindNode(span)
                    : null;

                if (syntaxNode != null && IsInsideNameOf(syntaxNode))
                    continue;

                var text = await loc.Document.GetTextAsync();
                var linePos = text.Lines.GetLineFromPosition(span.Start);
                var lineNumber = linePos.LineNumber + 1;
                var lineText = linePos.ToString().TrimStart();

                trueRefs.Add((filePath, lineNumber, lineText));
            }
        }

        if (trueRefs.Count > 0)
        {
            foreach (var (filePath, line, lineText) in trueRefs)
                Console.WriteLine($"{filePath}({line}): {lineText}");

            throw new InvalidOperationException(
                $"Cannot delete '{sym.Name}': it has {trueRefs.Count} reference(s).");
        }

        if (sym.DeclaringSyntaxReferences.Length == 0)
            throw new InvalidOperationException($"No declaring syntax found for '{sym.Name}'.");

        var syntaxRef = sym.DeclaringSyntaxReferences[0];
        var declaringNode = await syntaxRef.GetSyntaxAsync();

        var nodeToRemove = FindDeclarationNode(declaringNode);
        if (nodeToRemove == null)
            throw new InvalidOperationException(
                $"Cannot remove '{sym.Name}': unsupported syntax node type ({declaringNode.GetType().Name}).");

        var syntaxTree = declaringNode.SyntaxTree;
        var doc = sln.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.FilePath != null &&
                string.Equals(d.FilePath, syntaxTree.FilePath, StringComparison.OrdinalIgnoreCase));

        if (doc == null)
            throw new InvalidOperationException($"Cannot find document for '{sym.Name}'.");

        RtLog.Info("Deleting '{Symbol}' from {FilePath}...", sym.ToDisplayString(), doc.FilePath);

        var editor = await DocumentEditor.CreateAsync(doc);
        editor.RemoveNode(nodeToRemove);
        var newDoc = editor.GetChangedDocument();
        var newSln = newDoc.Project.Solution;

        await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun);
    }

    private static async Task<ISymbol> ResolveSymbolAsync(Solution sln, string symbol, string? kind)
    {
        if (symbol.Contains('.'))
            return await SymbolLocator.FindSymbolByFqnAsync(sln, symbol, kind);

        if (kind is null)
        {
            try
            {
                return await SymbolLocator.FindSymbolAsync(sln, symbol, "type");
            }
            catch (InvalidOperationException ex)
                when (ex.Message.StartsWith($"No symbol named '{symbol}'", StringComparison.Ordinal))
            {
                // Fall back to the broader lookup so explicit member-like simple names keep working.
            }
        }

        return await SymbolLocator.FindSymbolAsync(sln, symbol, kind);
    }

    internal static SyntaxNode? FindDeclarationNode(SyntaxNode node) =>
        node switch
        {
            ClassDeclarationSyntax => node,
            InterfaceDeclarationSyntax => node,
            StructDeclarationSyntax => node,
            RecordDeclarationSyntax => node,
            EnumDeclarationSyntax => node,
            DelegateDeclarationSyntax => node,
            MethodDeclarationSyntax => node,
            PropertyDeclarationSyntax => node,
            FieldDeclarationSyntax => node,
            EventDeclarationSyntax => node,
            _ => null
        };

    internal static bool IsInsideNameOf(SyntaxNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current is InvocationExpressionSyntax invocation)
            {
                if (invocation.Expression is IdentifierNameSyntax id &&
                    id.Identifier.ValueText == "nameof")
                    return true;
            }

            if (current is StatementSyntax or MemberDeclarationSyntax)
                break;

            current = current.Parent;
        }

        return false;
    }
}
