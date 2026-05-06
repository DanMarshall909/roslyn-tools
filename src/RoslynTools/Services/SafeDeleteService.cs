using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.FindSymbols;
using System.Diagnostics.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance class created by rt destatic-class migration.")]
internal class SafeDeleteService : IRtService
{
    private readonly ISymbolResolutionService _symbols;
    private readonly ILogger<SafeDeleteService> _logger;
    private readonly ISolutionChangeWriter _solutionChangeWriter;

    public SafeDeleteService()
        : this(new RtConsoleLogger<SafeDeleteService>(), new SolutionChangeWriter())
    {
    }

    public SafeDeleteService(ILogger<SafeDeleteService> logger, ISolutionChangeWriter solutionChangeWriter,
        ISymbolResolutionService? symbols = null)
    {
        _symbols = symbols ?? new SymbolResolutionService();

        _logger = logger;
        _solutionChangeWriter = solutionChangeWriter;
    }
    internal async Task RunAsync(Solution sln, string symbol, string? kind, bool dryRun)
    {
        var sym = await ResolveSymbolAsync(sln, symbol, kind);

        _logger.RtInfo("Checking references for '{Symbol}'...", sym.ToDisplayString());

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
                RtConsole.WriteLine(RtOutputContext.Result, $"{filePath}({line}): {lineText}");

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

        _logger.RtInfo("Deleting '{Symbol}' from {FilePath}...", sym.ToDisplayString(), doc.FilePath);

        var editor = await DocumentEditor.CreateAsync(doc);
        editor.RemoveNode(nodeToRemove);
        var newDoc = editor.GetChangedDocument();
        var newSln = newDoc.Project.Solution;

        await _solutionChangeWriter.ApplyChangesAsync(sln, newSln, dryRun);
    }

    private async Task<ISymbol> ResolveSymbolAsync(Solution sln, string symbol, string? kind)
    {
        if (symbol.Contains('.'))
            return await _symbols.FindSymbolByFqnAsync(sln, symbol, kind);

        if (kind is null)
        {
            try
            {
                return await _symbols.FindSymbolAsync(sln, symbol, "type");
            }
            catch (InvalidOperationException ex)
                when (ex.Message.StartsWith($"No symbol named '{symbol}'", StringComparison.Ordinal))
            {
                // Fall back to the broader lookup so explicit member-like simple names keep working.
            }
        }

        return await _symbols.FindSymbolAsync(sln, symbol, kind);
    }

    internal SyntaxNode? FindDeclarationNode(SyntaxNode node) =>
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

    internal bool IsInsideNameOf(SyntaxNode node)
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
