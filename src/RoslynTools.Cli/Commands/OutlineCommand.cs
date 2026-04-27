using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class OutlineCommand : IRtCommand
{
    private OutlineCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "outline",
        Mutates: false,
        JsonOutput: true,
        Category: "Navigation",
        Aliases: null,
        WhenToUse: "Getting a JSON summary of all types and member signatures in a file or namespace without reading the raw source. Useful for agents to orient themselves in an unfamiliar module before planning a refactor, or to diff the public surface area across branches.",
        Examples:
        [
            "rt outline src/backend/TourStore.Api/Controllers/Admin/AdminController.cs --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt outline ProjectX.Core.Services --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt outline RoslynTools.Commands --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var targetArg = new Argument<string>("target", "File path or namespace to outline");
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file");

        var cmd = new Command("outline", "List all types and member signatures in a file or namespace (JSON)")
        {
            targetArg, solutionOpt
        };

        cmd.SetHandler(async (target, solution) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                List<Dictionary<string, object?>> result;

                if (target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || Path.Exists(target))
                    result = await OutlineService.OutlineFileAsync(sln, target);
                else
                    result = await OutlineService.OutlineNamespaceAsync(sln, target);

                StructuredOutput.Write(result, SymbolFormatter.DefaultJsonOptions);
            }
        },
        targetArg, solutionOpt);

        return cmd;
    }
}
