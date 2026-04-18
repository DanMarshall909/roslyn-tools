using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal static class SafeDeleteCommand
{
    internal static Command Build()
    {
        var symbolOpt = new Option<string>("--symbol", "Type or member FQN or simple name to delete") { IsRequired = true };
        var kindOpt = new Option<string?>("--kind", "Kind of symbol: type, method, or property");
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("safe-delete", "Delete a type or member only if it has no references");
        cmd.AddAlias("sd");
        cmd.Add(symbolOpt);
        cmd.Add(kindOpt);
        cmd.Add(solutionOpt);
        cmd.Add(dryRunOpt);

        cmd.SetHandler(async (symbol, kind, solution, dryRun) =>
        {
            try
            {
                await ExecuteAsync(symbol, kind, solution, dryRun);
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, symbolOpt, kindOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(string symbol, string? kind, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            // Use FQN lookup when the symbol name contains a dot (e.g. "Svc.Dead"),
            // simple-name lookup otherwise.
            var sym = symbol.Contains('.')
                ? await SymbolLocator.FindSymbolByFqnAsync(sln, symbol, kind)
                : await SymbolLocator.FindSymbolAsync(sln, symbol, kind);

            Console.Error.WriteLine($"Checking references for '{sym.ToDisplayString()}'...");

            var referencedSymbols = await SymbolFinder.FindReferencesAsync(sym, sln);

            // Collect the declaration span so we can filter it out
            var declarationSpans = sym.DeclaringSyntaxReferences
                .Select(r => (r.SyntaxTree.FilePath, r.Span))
                .ToHashSet();

            var trueRefs = new List<(string FilePath, int Line, string LineText)>();

            foreach (var referencedSymbol in referencedSymbols)
            {
                foreach (var loc in referencedSymbol.Locations)
                {
                    var span = loc.Location.SourceSpan;
                    var filePath = loc.Document.FilePath ?? string.Empty;

                    // Skip the declaration itself
                    var declSyntaxRef = sym.DeclaringSyntaxReferences.FirstOrDefault(r =>
                        string.Equals(r.SyntaxTree.FilePath, filePath, StringComparison.OrdinalIgnoreCase)
                        && r.Span.Contains(span));
                    if (declSyntaxRef != null)
                        continue;

                    // Skip nameof() references
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

            // Zero references — find the declaring syntax node and remove it
            if (sym.DeclaringSyntaxReferences.Length == 0)
                throw new InvalidOperationException($"No declaring syntax found for '{sym.Name}'.");

            var syntaxRef = sym.DeclaringSyntaxReferences[0];
            var declaringNode = await syntaxRef.GetSyntaxAsync();

            // Walk up to the appropriate declaration node
            var nodeToRemove = FindDeclarationNode(declaringNode);
            if (nodeToRemove == null)
                throw new InvalidOperationException(
                    $"Cannot remove '{sym.Name}': unsupported syntax node type ({declaringNode.GetType().Name}).");

            // Find the document that contains this node
            var syntaxTree = declaringNode.SyntaxTree;
            var doc = sln.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath != null &&
                    string.Equals(d.FilePath, syntaxTree.FilePath, StringComparison.OrdinalIgnoreCase));

            if (doc == null)
                throw new InvalidOperationException($"Cannot find document for '{sym.Name}'.");

            Console.Error.WriteLine($"Deleting '{sym.ToDisplayString()}' from {doc.FilePath}...");

            var editor = await DocumentEditor.CreateAsync(doc);
            editor.RemoveNode(nodeToRemove);
            var newDoc = editor.GetChangedDocument();
            var newSln = newDoc.Project.Solution;

            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun);
        }
    }

    private static SyntaxNode? FindDeclarationNode(SyntaxNode node)
    {
        // Walk up from the node to find the best removal target
        return node switch
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
    }

    private static bool IsInsideNameOf(SyntaxNode node)
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

            // Stop at statement/declaration boundaries to avoid scanning too far up
            if (current is StatementSyntax or MemberDeclarationSyntax)
                break;

            current = current.Parent;
        }

        return false;
    }
}
