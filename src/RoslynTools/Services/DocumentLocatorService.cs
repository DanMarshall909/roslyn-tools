using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal interface IDocumentLocatorService
{
    DocumentId? FindDocumentId(Solution solution, ISymbol symbol);

    Task<TSyntax> GetDeclarationAsync<TSyntax>(Solution solution, ISymbol symbol)
        where TSyntax : SyntaxNode;

    Task<SyntaxNode> GetSyntaxRootAsync(Document document);
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance service registered through DI for dependency inversion.")]
internal sealed class DocumentLocatorService : IDocumentLocatorService, IRtService
{
    public DocumentId? FindDocumentId(Solution solution, ISymbol symbol) =>
        SymbolDocumentLocator.FindDocumentId(solution, symbol);

    public async Task<TSyntax> GetDeclarationAsync<TSyntax>(Solution solution, ISymbol symbol)
        where TSyntax : SyntaxNode
    {
        var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault()
            ?? throw new InvalidOperationException($"No source declaration found for '{symbol.ToDisplayString()}'.");

        var syntax = await syntaxRef.GetSyntaxAsync();
        if (syntax is not TSyntax typedSyntax)
        {
            throw new InvalidOperationException(
                $"Source declaration for '{symbol.ToDisplayString()}' is not a {typeof(TSyntax).Name}.");
        }

        return typedSyntax;
    }

    public async Task<SyntaxNode> GetSyntaxRootAsync(Document document) =>
        await document.GetSyntaxRootAsync()
        ?? throw new InvalidOperationException($"Cannot read syntax root for '{document.FilePath ?? document.Name}'.");
}
