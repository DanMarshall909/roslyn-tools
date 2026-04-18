using System.CommandLine;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class FindRefsCommand : IRtCommand
{
    private FindRefsCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "find-refs",
        Mutates: false,
        JsonOutput: false,
        Category: "Navigation",
        Aliases: null,
        WhenToUse: "Finding all references to any symbol (type, method, property, field, namespace) across the solution using Roslyn's semantic analysis. Prefer this over grep because it resolves overloads and type inference correctly and skips string literals/comments.",
        Examples:
        [
            "rt find-refs --symbol BookingService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt find-refs --symbol CreateBooking --kind method --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt find-refs --symbol IEmailService --kind type --solution RoslynTools.slnx",
        ],
        KnownIssues:
        [
            "Invoking via `cmd //c` from bash on Windows crashes during workspace load — see ai-improvements row #13.",
        ]
    );

    public static Command Build()
    {
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file");
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
