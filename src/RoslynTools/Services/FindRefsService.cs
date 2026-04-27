using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;
using RoslynTools.Services.Results;

namespace RoslynTools.Services;

internal static class FindRefsService
{
    internal static async Task<FindRefsResult> FindAsync(
        Solution sln,
        string symbol,
        string? kind)
    {
        var sym = await SymbolLocator.FindSymbolAsync(sln, symbol, kind);

        RtLog.Info("Finding references to '{Symbol}'...", sym.ToDisplayString());

        var referencedSymbols = await SymbolFinder.FindReferencesAsync(sym, sln);
        var references = new List<ReferenceResult>();

        foreach (var referencedSymbol in referencedSymbols)
        {
            foreach (var loc in referencedSymbol.Locations)
            {
                var doc = loc.Document;
                var text = await doc.GetTextAsync();
                var linePos = text.Lines.GetLineFromPosition(loc.Location.SourceSpan.Start);
                var lineNumber = linePos.LineNumber + 1;
                var lineText = linePos.ToString().TrimStart();

                references.Add(new ReferenceResult
                {
                    FilePath = doc.FilePath ?? string.Empty,
                    Line = lineNumber,
                    LineText = lineText,
                });
            }
        }

        return new FindRefsResult
        {
            SymbolName = sym.Name,
            References = references,
        };
    }
}
