using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal static class ReplaceCallCommand
{
    internal static Command Build()
    {
        var fromOpt = new Option<string>("--from", "Full method reference to replace, e.g. Logger.Log") { IsRequired = true };
        var toOpt = new Option<string>("--to", "New method name, e.g. LogInfo") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln file");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");

        var cmd = new Command("replace-call", "Replace all invocations of one static method with another")
        {
            fromOpt, toOpt, solutionOpt, dryRunOpt
        };
        cmd.AddAlias("rc");

        cmd.SetHandler(async (from, to, solution, dryRun) =>
        {
            await ExecuteAsync(from, to, solution, dryRun);
        },
        fromOpt, toOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(string from, string to, string? solutionPath, bool dryRun)
    {
        // Parse "TypeName.MethodName" — split on last dot
        var lastDot = from.LastIndexOf('.');
        var methodName = lastDot >= 0 ? from[(lastDot + 1)..] : from;

        var (workspace, solution) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            var originalSolution = solution;

            // Locate the method symbol using the full "TypeName.MethodName" form
            var methodSymbol = await SymbolLocator.FindSymbolByFqnAsync(solution, from, "method");

            Console.Error.WriteLine($"Finding invocations of '{methodSymbol.ToDisplayString()}'...");

            var referencedSymbols = await SymbolFinder.FindReferencesAsync(methodSymbol, solution);

            // Collect all invocation locations grouped by document
            var locationsByDocument = new Dictionary<DocumentId, List<Microsoft.CodeAnalysis.Location>>();
            int totalCallSites = 0;

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
                        locs = new List<Microsoft.CodeAnalysis.Location>();
                        locationsByDocument[doc.Id] = locs;
                    }

                    locs.Add(loc.Location);
                    totalCallSites++;
                }
            }

            if (totalCallSites == 0)
            {
                Console.WriteLine($"No calls to {from} found.");
                return;
            }

            Console.Error.WriteLine($"Found {totalCallSites} call site(s). Replacing '{methodName}' → '{to}'...");

            // Apply replacements per document in a single ReplaceNodes pass to avoid stale nodes
            foreach (var (docId, locations) in locationsByDocument)
            {
                var doc = solution.GetDocument(docId)!;
                var root = await doc.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                // Collect all nodes to replace: find the Name node in each invocation's expression
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

                if (replacements.Count == 0)
                    continue;

                var newRoot = root.ReplaceNodes(
                    replacements.Keys,
                    (original, _) => replacements[original]);

                solution = solution.WithDocumentSyntaxRoot(docId, newRoot);
            }

            await SolutionWriter.ApplyChangesAsync(originalSolution, solution, dryRun);
        }
    }
}
