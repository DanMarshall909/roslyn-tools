using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Replaces method calls with alternative implementations.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class ReplaceCallService : IRtService
{
    private readonly ISymbolResolutionService _symbols;

    public ReplaceCallService(ISymbolResolutionService symbols)
    {
        _symbols = symbols;
    }

    /// <summary>
    /// Runs an asynchronous method to replace call services in a solution with specified parameters.
    /// </summary>
    internal sealed record Preparation(Solution Solution, bool Changed, string? Message = null);

    internal async Task<Preparation> PrepareAsync(Solution solution, string from, string to, int? ifArgIndex = null,
        string? ifArgLiteral = null)
    {
        var methodSymbol = await _symbols.FindSymbolByFqnAsync(solution, from, "method");

        var referencedSymbols = await SymbolFinder.FindReferencesAsync(methodSymbol, solution);
        var locationsByDocument = await FindInvocationSitesAsync(referencedSymbols, ifArgIndex, ifArgLiteral);

        if (locationsByDocument.Count == 0)
        {
            return new Preparation(solution, false, $"No calls to {from} found.");
        }

        foreach (var (docId, locations) in locationsByDocument)
        {
            var doc = solution.GetDocument(docId)!;
            var root = await doc.GetSyntaxRootAsync();
            if (root is null)
                continue;

            var replacements = RewriteMemberAccess(root, locations, to);
            if (replacements.Count == 0)
                continue;

            var newRoot = root.ReplaceNodes(
                replacements.Keys,
                (original, _) => replacements[original]);

            solution = solution.WithDocumentSyntaxRoot(docId, newRoot);
        }

        return new Preparation(solution, true);
    }

    private async Task<Dictionary<DocumentId, List<Location>>> FindInvocationSitesAsync(
        IEnumerable<ReferencedSymbol> referencedSymbols,
        int? ifArgIndex = null,
        string? ifArgLiteral = null)
    {
        var locationsByDocument = new Dictionary<DocumentId, List<Location>>();

        foreach (var referencedSymbol in referencedSymbols)
            foreach (var loc in referencedSymbol.Locations)
            {
                var doc = loc.Document;
                var root = await doc.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                var node = root.FindNode(loc.Location.SourceSpan);
                var invocation = node.AncestorsAndSelf()
                    .OfType<InvocationExpressionSyntax>()
                    .FirstOrDefault();

                if (invocation is null)
                    continue;

                // Apply argument filter if specified
                if (ifArgIndex.HasValue && ifArgLiteral is not null)
                    if (!ArgumentFilter.MatchesStringLiteral(invocation.ArgumentList, ifArgIndex.Value, ifArgLiteral))
                        continue;

                if (!locationsByDocument.TryGetValue(doc.Id, out var locs))
                {
                    locs = new List<Location>();
                    locationsByDocument[doc.Id] = locs;
                }

                locs.Add(loc.Location);
            }

        return locationsByDocument;
    }

    private Dictionary<SyntaxNode, SyntaxNode> RewriteMemberAccess(
        SyntaxNode root,
        IEnumerable<Location> locations,
        string to)
    {
        var replacements = new Dictionary<SyntaxNode, SyntaxNode>();

        foreach (var loc in locations)
        {
            var node = root.FindNode(loc.SourceSpan);
            var invocation = node.AncestorsAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault();

            if (invocation is null)
                continue;

            SyntaxNode? oldName = null;
            SyntaxNode? newName = null;

            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                oldName = memberAccess.Name;
                newName = SyntaxFactory.IdentifierName(to)
                    .WithTriviaFrom(memberAccess.Name);
            }
            else if (invocation.Expression is IdentifierNameSyntax simpleName)
            {
                oldName = simpleName;
                newName = SyntaxFactory.IdentifierName(to)
                    .WithTriviaFrom(simpleName);
            }

            if (oldName is not null && newName is not null && !replacements.ContainsKey(oldName))
                replacements[oldName] = newName;
        }

        return replacements;
    }
}
