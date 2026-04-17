using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal static class FindCallersCommand
{
    internal static Command Build()
    {
        var symbolOpt = new Option<string>("--symbol", "Method name to find callers of") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var includeTestsOpt = new Option<bool>("--include-tests", "Include test projects in results");

        var cmd = new Command("find-callers", "Find all invocation sites for a method");
        cmd.AddAlias("callers");
        cmd.Add(symbolOpt);
        cmd.Add(solutionOpt);
        cmd.Add(includeTestsOpt);

        cmd.SetHandler(async (symbol, solution, includeTests) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var methodSymbol = await SymbolLocator.FindSymbolAsync(sln, symbol, "method");

                Console.Error.WriteLine($"Finding callers of '{methodSymbol.ToDisplayString()}'...");

                var referencedSymbols = await SymbolFinder.FindReferencesAsync(methodSymbol, sln);
                int total = 0;

                foreach (var referencedSymbol in referencedSymbols)
                {
                    foreach (var loc in referencedSymbol.Locations)
                    {
                        var doc = loc.Document;

                        if (!includeTests &&
                            IsTestProject(doc.Project.Name))
                        {
                            continue;
                        }

                        var root = await doc.GetSyntaxRootAsync();
                        if (root is null)
                            continue;

                        var node = root.FindNode(loc.Location.SourceSpan);

                        var invocation = node.AncestorsAndSelf()
                            .OfType<InvocationExpressionSyntax>()
                            .FirstOrDefault();

                        // Exclude nameof(...) — they are invocations syntactically but not real callers
                        if (invocation is null || IsNameofExpression(invocation))
                            continue;

                        var lineSpan = loc.Location.GetLineSpan();
                        var line = lineSpan.StartLinePosition.Line + 1;

                        var containingMethod = invocation.Ancestors()
                            .OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault()
                            ?.Identifier.Text ?? "<unknown>";

                        var text = await doc.GetTextAsync();
                        var sourceLine = text.Lines[line - 1].ToString().TrimStart();

                        Console.WriteLine($"{doc.FilePath}:{line}: {containingMethod}: {sourceLine}");
                        total++;
                    }
                }

                Console.Error.WriteLine($"Found {total} caller(s) of '{methodSymbol.Name}'.");
            }
        }, symbolOpt, solutionOpt, includeTestsOpt);

        return cmd;
    }

    internal static bool IsNameofExpression(InvocationExpressionSyntax invocation)
    {
        // nameof(...) has an IdentifierNameSyntax expression with text "nameof"
        return invocation.Expression is IdentifierNameSyntax id &&
               id.Identifier.ValueText == "nameof";
    }

    private static bool IsTestProject(string projectName) =>
        projectName.Contains("Test", StringComparison.OrdinalIgnoreCase);
}
