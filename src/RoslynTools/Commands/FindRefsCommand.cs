using System.CommandLine;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal static class FindRefsCommand
{
    internal static Command Build()
    {
        var solutionOpt = new Option<string>("--solution", "Path to the .sln file") { IsRequired = true };
        var symbolOpt = new Option<string>("--symbol", "Simple name of the symbol") { IsRequired = true };
        var kindOpt = new Option<string?>("--kind", "Symbol kind: type, method, property, field, namespace");

        var cmd = new Command("find-refs", "Find all references to a symbol across the solution")
        {
            solutionOpt, symbolOpt, kindOpt
        };

        cmd.SetHandler(async (solution, symbol, kind) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var sym = await SymbolLocator.FindSymbolAsync(sln, symbol, kind);

                Console.Error.WriteLine($"Finding references to '{sym.ToDisplayString()}'...");

                var referencedSymbols = await SymbolFinder.FindReferencesAsync(sym, sln);

                int total = 0;
                foreach (var referencedSymbol in referencedSymbols)
                {
                    foreach (var loc in referencedSymbol.Locations)
                    {
                        var doc = loc.Document;
                        var text = await doc.GetTextAsync();
                        var linePos = text.Lines.GetLineFromPosition(loc.Location.SourceSpan.Start);
                        var lineNumber = linePos.LineNumber + 1;
                        var lineText = linePos.ToString().TrimStart();

                        Console.WriteLine($"{doc.FilePath}({lineNumber}): {lineText}");
                        total++;
                    }
                }

                Console.WriteLine($"\nFound {total} reference(s) to '{sym.Name}'.");
            }
        },
        solutionOpt, symbolOpt, kindOpt);

        return cmd;
    }
}
