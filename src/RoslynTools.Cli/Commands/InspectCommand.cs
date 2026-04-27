using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class InspectCommand : IRtCommand
{
    private InspectCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "inspect",
        Mutates: false,
        JsonOutput: true,
        Category: "inspect",
        Aliases: null,
        WhenToUse: "Getting a detailed JSON description of a symbol's shape — members, base types, interfaces, constructors, locations — without opening the file. Ideal for agents that need to understand a type's surface area before planning a refactor.",
        Examples:
        [
            "rt inspect inspect BookingService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect inspect ProjectX.Core.Services.IEmailService --kind type --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect inspect IRtCommand --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var symbolArg = new Argument<string>("symbol", "Fully-qualified symbol name to inspect");
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file");
        var kindOpt = new Option<string?>("--kind", "Symbol kind: type, method, property, field, namespace");

        var cmd = new Command("inspect", "Show type shape: members, base types, interfaces, locations (JSON)")
        {
            symbolArg, solutionOpt, kindOpt
        };

        cmd.SetHandler(async (symbol, solution, kind) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, symbol, kind);
                var result = InspectService.BuildInspection(sym, sln);
                StructuredOutput.Write(result, SymbolFormatter.DefaultJsonOptions);
            }
        },
        symbolArg, solutionOpt, kindOpt);

        return cmd;
    }
}
