using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class SourceCommand : IRtCommand
{
    private SourceCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "source",
        false,
        true,
        "inspect",
        Aliases: null,
        WhenToUse:
        "Reading the full source of a specific method, property, type, or field by FQN without opening the file. Returns the declaration including XML doc comments, with file path and line numbers. Prefer this over Read when you know the exact member you need.",
        Examples:
        [
            "rt inspect source RoslynTools.Services.OutlineService.BuildTypeSummary --solution RoslynTools.slnx",
            "rt inspect source RoslynTools.Core.WorkspaceLoader --solution RoslynTools.slnx",
            "rt inspect source ProjectX.Core.Services.BookingService.CreateBookingAsync --kind method --solution C:\\code\\libraries\\ProjectX.Libraries.sln"
        ]
    );

    public static Command Build()
    {
        var symbolArg = new Argument<string>("symbol", "Fully-qualified member or type name");
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file");
        var kindOpt = new Option<string?>("--kind", "Symbol kind hint: type, method, property, field");

        var cmd = new Command("source", "Print the source code of a member or type by FQN (JSON)")
        {
            symbolArg, solutionOpt, kindOpt
        };

        cmd.SetHandler(async (symbol, solution, kind) =>
            {
                var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
                using (workspace)
                {
                    var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, symbol, kind);
                    var result = await RtServiceProvider.Get<InspectService>().GetSourceAsync(sym);
                    StructuredOutput.Write(result, SymbolFormatter.DefaultJsonOptions);
                }
            },
            symbolArg, solutionOpt, kindOpt);

        return cmd;
    }
}
