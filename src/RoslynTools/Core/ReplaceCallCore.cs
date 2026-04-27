using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.CSharp;

namespace RoslynTools.Core;

internal static class ReplaceCallCore
{
    internal static async Task RunAsync(Solution solution, string from, string to, bool dryRun)
    {
        var originalSolution = solution;

        var methodName = ParseMethodName(from);
        var methodSymbol = await SymbolLocator.FindSymbolByFqnAsync(solution, from, "method");

        RtLog.Info("Finding invocations of '{Method}'...", methodSymbol.ToDisplayString());

        var referencedSymbols = await SymbolFinder.FindReferencesAsync(methodSymbol, solution);
        var locationsByDocument = await FindInvocationSitesAsync(referencedSymbols);

        if (locationsByDocument.Count == 0)
        {
            Console.WriteLine($"No calls to {from} found.");
            return;
        }

        var totalCallSites = locationsByDocument.Values.Sum(l => l.Count);
        RtLog.Info("Found {TotalCallSites} call site(s). Replacing '{MethodName}' → '{To}'...", totalCallSites, methodName, to);

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

        await SolutionWriter.ApplyChangesAsync(originalSolution, solution, dryRun);
    }

    private static string ParseMethodName(string from)
    {
        var lastDot = from.LastIndexOf('.');
        return lastDot >= 0 ? from[(lastDot + 1)..] : from;
    }

    private static async Task<Dictionary<DocumentId, List<Location>>> FindInvocationSitesAsync(
        IEnumerable<ReferencedSymbol> referencedSymbols)
    {
        var locationsByDocument = new Dictionary<DocumentId, List<Location>>();

        foreach (var referencedSymbol in referencedSymbols)
        {
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

                if (!locationsByDocument.TryGetValue(doc.Id, out var locs))
                {
                    locs = new List<Location>();
                    locationsByDocument[doc.Id] = locs;
                }

                locs.Add(loc.Location);
            }
        }

        return locationsByDocument;
    }

    private static Dictionary<SyntaxNode, SyntaxNode> RewriteMemberAccess(
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
            {
                replacements[oldName] = newName;
            }
        }

        return replacements;
    }
}
