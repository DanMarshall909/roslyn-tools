using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using RoslynTools.Core;
using RoslynTools.Services.Results;

namespace RoslynTools.Services;

/// <summary>
/// Provides methods for finding references to a given type symbol.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class FindRefsService : IRtService
{
    private readonly ILogger<FindRefsService> _logger;
    private readonly ISymbolCandidateService _symbols;

    public FindRefsService(
        ILogger<FindRefsService> logger,
        ISymbolCandidateService symbols)
    {
        _symbols = symbols;
        _logger = logger;
    }

    /// <summary>
    /// Async method to find references based on a string and an optional flag.
    /// </summary>
    internal async Task<FindRefsResult> FindAsync(
        Solution sln,
        string symbol,
        string? kind)
    {
        var sym = await ResolveSymbolAsync(sln, symbol, kind);

        _logger.RtInfo("Finding references to '{Symbol}'...", sym.ToDisplayString());

        var referencedSymbols = await SymbolFinder.FindReferencesAsync(sym, sln);
        var references = new List<ReferenceResult>();

        foreach (var referencedSymbol in referencedSymbols)
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
                    LineText = lineText
                });
            }

        return new FindRefsResult
        {
            SymbolName = sym.Name,
            FullyQualifiedName = sym.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            References = references
        };
    }

    private async Task<ISymbol> ResolveSymbolAsync(Solution sln, string symbol, string? kind)
    {
        return (await _symbols.ResolveAsync(sln, symbol, kind)).Unwrap();
    }
}
