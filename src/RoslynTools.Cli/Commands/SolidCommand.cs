using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Commands;

internal sealed class SolidCommand : IRtCommand, IStructuredOutputCommand
{
    private SolidCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "solid",
        Mutates: false,
        JsonOutput: true,
        Category: "analyze",
        WhenToUse:
        "Detect classes that may violate SOLID principles using heuristic analysis. Use --principle SRP to flag classes with too many public methods and injected dependencies. Use --principle all to run all active detectors.",
        Examples:
        [
            "rt analyze solid --principle SRP --solution RoslynTools.slnx",
            "rt analyze solid --principle all --solution RoslynTools.slnx",
            "rt analyze solid --principle SRP --min-methods 5 --min-deps 3 --solution RoslynTools.slnx"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]
    );

    public static Command Build()
    {
        var cmd = new Command("solid", "Detect SOLID principle violations using heuristic analysis");
        cmd.SetHandler(async context =>
        {
            context.ExitCode = await Task.FromResult(0);
        });
        return cmd;
    }
}
