using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;
using RoslynTools.Services.Results;
using System.Diagnostics.CodeAnalysis;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance class created by rt destatic-class migration.")]
internal class FindCallersService
{
    internal async Task<FindCallersResult> FindAsync(
        Solution sln,
        string symbol,
        bool includeTests)
    {
        var methodSymbol = await SymbolLocator.FindSymbolAsync(sln, symbol, "method");

        RtLog.Info("Finding callers of '{Method}'...", methodSymbol.ToDisplayString());

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

        RtLog.Info("Found {Count} caller(s) of '{Name}'.", callers.Count, methodSymbol.Name);

        return new FindCallersResult
        {
            SymbolName = methodSymbol.Name,
            Callers = callers,
        };
    }

    internal bool IsNameofExpression(InvocationExpressionSyntax invocation) =>
        invocation.Expression is IdentifierNameSyntax id &&
        id.Identifier.ValueText == "nameof";

    internal bool IsTestProject(string projectName) =>
        projectName.Contains("Test", StringComparison.OrdinalIgnoreCase);
}
