using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Provides a description of a command or its usage.
/// </summary>
internal sealed class DescribeCommand : IRtCommand, IStructuredOutputCommand
{
    private DescribeCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "describe",
        false,
        true,
        "inspect",
        Aliases: null,
        WhenToUse:
        "Getting a detailed JSON description of a symbol's shape — members, base types, interfaces, constructors, locations — without opening the file. Ideal for agents that need to understand a type's surface area before planning a refactor.",
        Examples:
        [
            "rt inspect describe BookingService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect describe ProjectX.Core.Services.IEmailService --kind type --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect describe IRtCommand --solution RoslynTools.slnx"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]
    );

    public static Command Build()
    {
        var symbolArg = new Argument<string>("symbol", "Fully-qualified symbol name to inspect");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln/.slnx file");
        var kindOpt = new Option<string?>("--kind", "Symbol kind: type, method, property, field, namespace");

        var cmd = new Command("describe", "Show type shape: members, base types, interfaces, locations (JSON)")
        {
            symbolArg, solutionOpt, kindOpt
        };

        cmd.SetHandler(async (symbol, solution, kind) =>
            {
                var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
                using (workspace)
                {
                    var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, symbol, kind);
                    var result = RtServiceProvider.Get<InspectService>().BuildInspection(sym, sln);
                    StructuredOutput.Write(result, SymbolFormatter.DefaultJsonOptions);
                }
            },
            symbolArg, solutionOpt, kindOpt);

        return cmd;
    }
}
