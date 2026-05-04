using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.CSharp;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal static class ReplaceNewService
{
    internal static async Task RunAsync(Solution solution, string typeName, string with_method, bool dryRun, int? ifArgIndex = null, string? ifArgLiteral = null)
    {
        var originalSolution = solution;

        RtLog.Info("Finding constructor invocations of '{Type}'...", typeName);

        var objectCreations = await FindObjectCreationsAsync(solution, typeName, ifArgIndex, ifArgLiteral);

        if (objectCreations.Count == 0)
        {
            RtConsole.WriteLine(RtOutputContext.Result, $"No constructor calls to {typeName} found.");
            return;
        }

        var totalCallSites = objectCreations.Values.Sum(l => l.Count);
        RtLog.Info("Found {TotalCallSites} constructor call site(s). Replacing '{TypeName}' → '{With}'...", totalCallSites, typeName, with_method);

        foreach (var (docId, locations) in objectCreations)
        {
            var doc = solution.GetDocument(docId)!;
            var root = await doc.GetSyntaxRootAsync();
            if (root is null)
                continue;

            var replacements = RewriteObjectCreation(root, locations, with_method);
            if (replacements.Count == 0)
                continue;

            var newRoot = root.ReplaceNodes(
                replacements.Keys,
                (original, _) => replacements[original]);

            solution = solution.WithDocumentSyntaxRoot(docId, newRoot);
        }

        await SolutionWriter.ApplyChangesAsync(originalSolution, solution, dryRun);
    }

    private static async Task<Dictionary<DocumentId, List<Location>>> FindObjectCreationsAsync(
        Solution solution,
        string typeName,
        int? ifArgIndex = null,
        string? ifArgLiteral = null)
    {
        var locationsByDocument = new Dictionary<DocumentId, List<Location>>();

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                var creations = root.DescendantNodes()
                    .OfType<ObjectCreationExpressionSyntax>()
                    .Where(oc => MatchesType(oc, typeName))
                    .ToList();

                foreach (var creation in creations)
                {
                    if (ifArgIndex.HasValue && ifArgLiteral is not null)
                    {
                        if (!ArgumentMatchesFilter(creation, ifArgIndex.Value, ifArgLiteral))
                            continue;
                    }

                    var location = creation.GetLocation();
                    if (!locationsByDocument.TryGetValue(document.Id, out var locs))
                    {
                        locs = new List<Location>();
                        locationsByDocument[document.Id] = locs;
                    }

                    locs.Add(location);
                }
            }
        }

        return locationsByDocument;
    }

    private static bool MatchesType(ObjectCreationExpressionSyntax creation, string typeName)
    {
        var creationTypeText = creation.Type.ToString();
        return creationTypeText == typeName;
    }

    private static bool ArgumentMatchesFilter(ObjectCreationExpressionSyntax creation, int argIndex, string literal)
    {
        if (creation.ArgumentList is null)
            return false;

        if (argIndex < 0 || argIndex >= creation.ArgumentList.Arguments.Count)
            return false;

        var arg = creation.ArgumentList.Arguments[argIndex];
        if (arg.Expression is not LiteralExpressionSyntax lit)
            return false;

        if (lit.Kind() != SyntaxKind.StringLiteralExpression)
            return false;

        var literalText = lit.Token.ValueText;
        return literalText == literal;
    }

    private static Dictionary<SyntaxNode, SyntaxNode> RewriteObjectCreation(
        SyntaxNode root,
        IEnumerable<Location> locations,
        string with_method)
    {
        var replacements = new Dictionary<SyntaxNode, SyntaxNode>();

        foreach (var loc in locations)
        {
            var node = root.FindNode(loc.SourceSpan);
            var creation = node as ObjectCreationExpressionSyntax
                ?? node.AncestorsAndSelf().OfType<ObjectCreationExpressionSyntax>().FirstOrDefault();

            if (creation is null)
                continue;

            var argList = creation.ArgumentList ?? SyntaxFactory.ArgumentList();
            var methodExpr = SyntaxFactory.ParseExpression(with_method);
            var newCall = SyntaxFactory.InvocationExpression(methodExpr, argList)
                .WithTriviaFrom(creation);

            if (!replacements.ContainsKey(creation))
            {
                replacements[creation] = newCall;
            }
        }

        return replacements;
    }
}
