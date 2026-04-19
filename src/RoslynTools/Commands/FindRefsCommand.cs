using System.CommandLine;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;

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
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]
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

                var result = new FindRefsResult
                {
                    SymbolName = sym.Name,
                    References = references,
                };

                RtSerializer.Write(result);
            }
        },
        solutionOpt, symbolOpt, kindOpt);

        return cmd;
    }
}
