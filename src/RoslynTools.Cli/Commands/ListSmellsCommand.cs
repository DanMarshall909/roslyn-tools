using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Commands;

internal sealed class ListSmellsCommand : IRtCommand
{
    private ListSmellsCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "list-smells",
        false,
        false,
        "analyze",
        Aliases: ["smells"],
        WhenToUse:
        "Getting a prioritised list of code smells — oversized classes, long methods, and constructors with too many parameters — to guide a refactor session. Use --scope to focus on your domain code and skip generated or test noise.",
        Examples:
        [
            "rt analyze list-smells --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt analyze smells --scope ProjectX.Core --max-class-lines 200 --max-method-lines 30 --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt analyze list-smells --solution RoslynTools.slnx --max-params 5"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]
    );

    public static Command Build()
    {
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var maxClassOpt = new Option<int>("--max-class-lines", () => 300, "Max lines per class");
        var maxMethodOpt = new Option<int>("--max-method-lines", () => 40, "Max lines per method");
        var maxParamsOpt = new Option<int>("--max-params", () => 7, "Max constructor params");
        var scopeOpt = new Option<string?>("--scope", "Namespace/project prefix filter");

        var cmd = new Command("list-smells",
            "Report code smells: large classes, long methods, over-injected constructors");
        cmd.AddAlias("smells");
        cmd.AddAll(solutionOpt, maxClassOpt, maxMethodOpt, maxParamsOpt, scopeOpt);

        cmd.SetHandler(async (solution, maxClass, maxMethod, maxParams, scope) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                RtLog.Info("Analyzing solution for code smells...");

                var smells = await SmellAnalyzer.AnalyzeAsync(sln, maxClass, maxMethod, maxParams, scope);

                var result = new ListSmellsResult
                {
                    Smells = smells.Select(s => new SmellItemResult
                    {
                        SmellType = s.SmellType,
                        TypeName = s.TypeName,
                        MemberName = s.MemberName,
                        FilePath = s.FilePath ?? string.Empty,
                        Line = s.Line,
                        Detail = s.Detail
                    }).ToList()
                };

                RtSerializer.Write(result);

                RtLog.Info("Found {Count} smell(s).", smells.Count);
            }
        }, solutionOpt, maxClassOpt, maxMethodOpt, maxParamsOpt, scopeOpt);

        return cmd;
    }
}
