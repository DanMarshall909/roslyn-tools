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
        var flags = RtCommandFlagBinder.Create()
            .WithSolution("Path to .sln/.slnx")
            .WithScope("Namespace/project prefix filter")
            .WithTop("Return only top N smell results (0 = all)");
        var maxClassOpt = new Option<int>("--max-class-lines", () => 300, "Max lines per class");
        var maxMethodOpt = new Option<int>("--max-method-lines", () => 40, "Max lines per method");
        var maxParamsOpt = new Option<int>("--max-params", () => 7, "Max constructor params");

        var cmd = new Command("list-smells",
            "Report code smells: large classes, long methods, over-injected constructors");
        cmd.AddAlias("smells");
        flags.AddTo(cmd);
        cmd.AddAll(maxClassOpt, maxMethodOpt, maxParamsOpt);

        cmd.SetHandler(async ctx =>
        {
            var maxClass = ctx.ParseResult.GetValueForOption(maxClassOpt);
            var maxMethod = ctx.ParseResult.GetValueForOption(maxMethodOpt);
            var maxParams = ctx.ParseResult.GetValueForOption(maxParamsOpt);
            var request = flags.Bind(
                ctx.ParseResult,
                f => ListSmellsCliRequest.FromFlags(f, maxClass, maxMethod, maxParams));

            var sp = ctx.BindingContext.GetService<IServiceProvider>();
            var loader = sp?.GetService<IWorkspaceSessionFactory>() ?? IWorkspaceSessionFactory.CreateDefault();
            var sln = await loader.GetSolutionAsync(request.SolutionPath);

            RtLog.Info("Analyzing solution for code smells...");

            var smells = await SmellAnalyzer.AnalyzeAsync(
                sln,
                request.MaxClassLines,
                request.MaxMethodLines,
                request.MaxParams,
                request.Scope);

            var resultSmells = request.Top > 0
                ? smells.Take(request.Top).ToList()
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

            RtResultRenderer.Write(RtResult<ListSmellsResult>.Ok(result));

            RtLog.Info("Found {Count} smell(s).", smells.Count);
        });

        return cmd;
    }

    private sealed record ListSmellsCliRequest(
        string? SolutionPath,
        string? Scope,
        int Top,
        int MaxClassLines,
        int MaxMethodLines,
        int MaxParams) : ISupportsSolutionOption, ISupportsScopeOption, ISupportsTopOption
    {
        public static ListSmellsCliRequest FromFlags(
            RtFlagValues flags,
            int maxClassLines,
            int maxMethodLines,
            int maxParams)
        {
            return new ListSmellsCliRequest(
                flags.SolutionPath,
                flags.Scope,
                flags.Top,
                maxClassLines,
                maxMethodLines,
                maxParams);
        }
    }
}
