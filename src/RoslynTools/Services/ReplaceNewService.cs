using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// A class used to replace new services in a solution.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class ReplaceNewService : IRtService
{
    private readonly ILogger<ReplaceNewService> _logger;
    private readonly ISolutionChangeWriter _solutionChangeWriter;

    public ReplaceNewService()
        : this(new RtConsoleLogger<ReplaceNewService>(), new SolutionChangeWriter())
    {
    }

    public ReplaceNewService(ILogger<ReplaceNewService> logger, ISolutionChangeWriter solutionChangeWriter)
    {
        _logger = logger;
        _solutionChangeWriter = solutionChangeWriter;
    }

    /// <summary>
    /// Runs an asynchronous method to replace new services in a solution with specified parameters.
    /// </summary>
    internal async Task RunAsync(Solution solution, string typeName, string with_method, bool dryRun,
        int? ifArgIndex = null, string? ifArgLiteral = null, bool dropMatchedArg = false)
    {
        var originalSolution = solution;

        _logger.RtInfo("Finding constructor invocations of '{Type}'...", typeName);

        var objectCreations = await FindObjectCreationsAsync(solution, typeName, ifArgIndex, ifArgLiteral);

        if (objectCreations.Count == 0)
        {
            RtConsole.WriteLine(RtOutputContext.Result, $"No constructor calls to {typeName} found.");
            return;
        }

        var totalCallSites = objectCreations.Values.Sum(l => l.Count);
        _logger.RtInfo("Found {TotalCallSites} constructor call site(s). Replacing '{TypeName}' → '{With}'...",
            totalCallSites, typeName, with_method);

        foreach (var (docId, locations) in objectCreations)
        {
            var doc = solution.GetDocument(docId)!;
            var root = await doc.GetSyntaxRootAsync();
            if (root is null)
                continue;

            var replacements = RewriteObjectCreation(root, locations, with_method, ifArgIndex, dropMatchedArg);
            if (replacements.Count == 0)
                continue;

            var newRoot = root.ReplaceNodes(
                replacements.Keys,
                (original, _) => replacements[original]);

            solution = solution.WithDocumentSyntaxRoot(docId, newRoot);
        }

        await _solutionChangeWriter.ApplyChangesAsync(originalSolution, solution, dryRun);
    }

    private async Task<Dictionary<DocumentId, List<Location>>> FindObjectCreationsAsync(
        Solution solution,
        string typeName,
        int? ifArgIndex = null,
        string? ifArgLiteral = null)
    {
        var locationsByDocument = new Dictionary<DocumentId, List<Location>>();

        foreach (var project in solution.Projects)
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
                        if (!ArgumentFilter.MatchesStringLiteral(creation.ArgumentList, ifArgIndex.Value, ifArgLiteral))
                            continue;

                    var location = creation.GetLocation();
                    if (!locationsByDocument.TryGetValue(document.Id, out var locs))
                    {
                        locs = new List<Location>();
                        locationsByDocument[document.Id] = locs;
                    }

                    locs.Add(location);
                }
            }

        return locationsByDocument;
    }

    private bool MatchesType(ObjectCreationExpressionSyntax creation, string typeName)
    {
        var creationTypeText = creation.Type.ToString();
        return creationTypeText == typeName;
    }

    private Dictionary<SyntaxNode, SyntaxNode> RewriteObjectCreation(
        SyntaxNode root,
        IEnumerable<Location> locations,
        string with_method,
        int? dropArgIndex = null,
        bool dropMatchedArg = false)
    {
        var replacements = new Dictionary<SyntaxNode, SyntaxNode>();

        foreach (var loc in locations)
        {
            var node = root.FindNode(loc.SourceSpan);
            var creation = node as ObjectCreationExpressionSyntax
                           ?? node.AncestorsAndSelf().OfType<ObjectCreationExpressionSyntax>().FirstOrDefault();

            if (creation is null)
                continue;

            var argList = BuildArgList(creation, dropArgIndex, dropMatchedArg);
            var methodExpr = SyntaxFactory.ParseExpression(with_method);
            var newCall = SyntaxFactory.InvocationExpression(methodExpr, argList)
                .WithTriviaFrom(creation);

            if (!replacements.ContainsKey(creation)) replacements[creation] = newCall;
        }

        return replacements;
    }

    private static ArgumentListSyntax BuildArgList(
        ObjectCreationExpressionSyntax creation,
        int? dropArgIndex,
        bool dropMatchedArg)
    {
        var original = creation.ArgumentList ?? SyntaxFactory.ArgumentList();

        if (!dropMatchedArg || dropArgIndex is null)
            return original;

        var idx = dropArgIndex.Value;
        if (idx < 0 || idx >= original.Arguments.Count)
            return original;

        var newArgs = original.Arguments.RemoveAt(idx);

        if (newArgs.Count > 0)
        {
            var first = newArgs[0].WithLeadingTrivia(SyntaxTriviaList.Empty);
            newArgs = newArgs.Replace(newArgs[0], first);
        }

        return original.WithArguments(newArgs);
    }
}
