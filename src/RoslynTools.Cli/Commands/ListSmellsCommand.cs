using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Commands;

/// <summary>
/// Lists all smells detected in the current file
/// </summary>
internal sealed class ListSmellsCommand : IRtCommand, IAnalyzeCommand
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
            "rt analyze list-smells --solution RoslynTools.slnx --max-params 5",
            "rt analyze list-smells --solution RoslynTools.slnx --scope RoslynTools --top 30"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]
    );

    public static Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var maxClassOpt = new Option<int>("--max-class-lines", () => 300, "Max lines per class");
        var maxMethodOpt = new Option<int>("--max-method-lines", () => 40, "Max lines per method");
        var maxParamsOpt = new Option<int>("--max-params", () => 7, "Max constructor params");
        var scopeOpt = new Option<string?>("--scope", "Namespace/project prefix filter");
        var topOpt = new Option<int>("--top", () => 0, "Return only top N smell results (0 = all)");

        var cmd = new Command("list-smells",
            "Report code smells: large classes, long methods, over-injected constructors");
        cmd.AddAlias("smells");
        cmd.AddAll(solutionOpt, maxClassOpt, maxMethodOpt, maxParamsOpt, scopeOpt, topOpt);

        cmd.SetHandler(async ctx =>
        {
            var solution = ctx.ParseResult.GetValueForOption(solutionOpt);
            var maxClass = ctx.ParseResult.GetValueForOption(maxClassOpt);
            var maxMethod = ctx.ParseResult.GetValueForOption(maxMethodOpt);
            var maxParams = ctx.ParseResult.GetValueForOption(maxParamsOpt);
            var scope = ctx.ParseResult.GetValueForOption(scopeOpt);
            var top = ctx.ParseResult.GetValueForOption(topOpt);

            if (top < 0)
            {
                RtLog.Error("--top must be >= 0.");
                Environment.ExitCode = 1;
                return;
            }

            var sp = ctx.BindingContext.GetService<IServiceProvider>();
            var loader = sp?.GetService<IWorkspaceSessionFactory>() ?? IWorkspaceSessionFactory.CreateDefault();
            var sln = await loader.GetSolutionAsync(solution);

            RtLog.Info("Analyzing solution for code smells...");

            var smells = await SmellAnalyzer.AnalyzeAsync(sln, maxClass, maxMethod, maxParams, scope);

            var resultSmells = top > 0
                ? smells.Take(top).ToList()
                : smells;

            var result = new ListSmellsResult
            {
                Smells = resultSmells.Select(s => new SmellItemResult
                {
                    SmellType = s.SmellType,
                    TypeName = s.TypeName,
                    MemberName = s.MemberName,
                    FilePath = s.FilePath ?? string.Empty,
                    Line = s.Line,
                    Detail = s.Detail,
                    SuggestedFixes = FixCatalog.ForSmell(s.SmellType, s.TypeName, s.FilePath, s.Line, sln.FilePath)
                }).ToList()
            };

            RtResultRenderer.WriteOutput(RtResult<ListSmellsResult>.Ok(result));

            RtLog.Info("Found {Count} smell(s).", smells.Count);
        });

        return cmd;
    }
}
