using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class ListImportsCommand : IRtCommand
{
    private ListImportsCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "list-imports",
        false,
        false,
        "analyze",
        Aliases: ["imports"],
        WhenToUse:
        "Auditing C# using directives, especially global using and using static directives, before consolidating imports or introducing static member imports.",
        Examples:
        [
            "rt analyze list-imports --kind global --solution RoslynTools.slnx",
            "rt analyze imports --kind static --solution C:\\code\\app\\App.sln",
            "rt analyze list-imports --kind all --json --solution RoslynTools.slnx"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]
    );

    public static Command Build()
    {
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var kindOpt = new Option<string>("--kind", () => "all", "Filter: all, global, static, or alias");

        var cmd = new Command("list-imports", "List using directives, including global and static imports");
        cmd.AddAlias("imports");
        cmd.AddAll(solutionOpt, kindOpt);

        cmd.SetHandler(async (solution, kind) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var imports = await RtServiceProvider.Get<ImportDirectiveService>().AnalyzeAsync(sln, kind);
                RtSerializer.Write(ImportDirectiveResult.FromImports(imports));
            }
        }, solutionOpt, kindOpt);

        return cmd;
    }
}
