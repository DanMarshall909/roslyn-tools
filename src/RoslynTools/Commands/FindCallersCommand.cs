using System.CommandLine;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Commands;

internal sealed class FindCallersCommand : IRtCommand
{
    private FindCallersCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "find-callers",
        Mutates: false,
        JsonOutput: false,
        Category: "Navigation",
        Aliases: ["callers"],
        WhenToUse: "Finding all invocation sites of a method before renaming or deleting it, or to understand the blast radius of a signature change. Unlike find-refs, this filters to true call sites only, excluding nameof() and other non-invocation references.",
        Examples:
        [
            "rt find-callers --symbol SendEmail --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt callers --symbol ProcessPayment --include-tests --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt find-callers --symbol BuildGraphAsync --solution RoslynTools.slnx",
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]
    );

    public static Command Build()
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
                var callers = new List<CallerResult>();

                foreach (var referencedSymbol in referencedSymbols)
                {
                    foreach (var loc in referencedSymbol.Locations)
                    {
                        var doc = loc.Document;

                        if (!includeTests && IsTestProject(doc.Project.Name))
                            continue;

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

                        callers.Add(new CallerResult
                        {
                            FilePath = doc.FilePath ?? string.Empty,
                            Line = line,
                            ContainingMethod = containingMethod,
                            SourceLine = sourceLine,
                        });
                    }
                }

                var result = new FindCallersResult
                {
                    SymbolName = methodSymbol.Name,
                    Callers = callers,
                };

                RtSerializer.Write(result);

                Console.Error.WriteLine($"Found {callers.Count} caller(s) of '{methodSymbol.Name}'.");
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
