using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Commands;

/// <summary>
/// Computes code complexity metrics for a solution.
/// </summary>
internal sealed class ComplexityCommand(IWorkspaceSessionFactory sessionFactory) : IRtCommand, IStructuredOutputCommand, IAnalyzeCommand
{
    internal static readonly string[] ValidSortValues = ["cyclomatic", "cognitive", "loc", "params"];
    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;

    public static RtCommandInfo Info { get; } = new(
        "complexity",
        false,
        true,
        "analyze",
        Aliases: ["cx"],
        WhenToUse:
        "Measuring cyclomatic and cognitive complexity, lines of code, and parameter counts for methods in a solution. Use --threshold to surface only the most complex methods, --sort to rank by the metric of interest, and --top to limit output.",
        Examples:
        [
            "rt analyze complexity --solution RoslynTools.slnx",
            "rt analyze cx --threshold 10 --sort cyclomatic --solution RoslynTools.slnx",
            "rt analyze complexity --threshold 5 --sort cognitive --top 20 --solution C:\\code\\libraries\\ProjectX.Libraries.sln"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]
    );

    public Command Build()
    {
        var flags = RtCommandFlagBinder.Create()
            .WithSolution("Path to .sln/.slnx")
            .WithTop("Return only top N results (0 = all)");
        var thresholdOpt = new Option<int>("--threshold", () => 0,
            "Minimum metric value to include a method (0 = include all)");
        var sortOpt =
            new Option<string>("--sort", () => "cyclomatic", "Sort metric: cyclomatic, cognitive, loc, params");

        var cmd = new Command("complexity",
            "Measure cyclomatic/cognitive complexity, LOC, and param counts per method");
        cmd.AddAlias("cx");
        flags.AddTo(cmd);
        cmd.AddAll(thresholdOpt, sortOpt);

        cmd.SetHandler(async context =>
        {
            var threshold = context.ParseResult.GetValueForOption(thresholdOpt);
            var sort = context.ParseResult.GetValueForOption(sortOpt)!;
            var request = flags.Bind(context.ParseResult, f => ComplexityCliRequest.FromFlags(f, threshold, sort));
            context.ExitCode = await ExecuteAsync(
                request.SolutionPath,
                request.Threshold,
                request.Sort,
                request.Top);
        });

        return cmd;
    }

    /// <summary>
    /// Executes complexity analysis with optional parameters.
    /// </summary>
    internal async Task<int> ExecuteAsync(string? solution, int threshold, string sort, int top)
    {
        if (threshold < 0)
        {
            RtLog.Error("--threshold must be >= 0.");
            return 1;
        }

        if (top < 0)
        {
            RtLog.Error("--top must be >= 0.");
            return 1;
        }

        if (!ValidSortValues.Contains(sort, StringComparer.OrdinalIgnoreCase))
        {
            RtLog.Error("--sort must be one of: {Values}.", string.Join(", ", ValidSortValues));
            return 1;
        }

        var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solution);
        using (workspace)
        {
            RtLog.Info("Analyzing complexity...");

            var report = await ComplexityAnalyzer.AnalyzeAsync(sln, threshold, sort, top);

            var result = new ComplexityResult
            {
                Methods = report.Methods.Select(m => new MethodComplexityItem
                {
                    Method = m.Method,
                    Type = m.Type,
                    Namespace = m.Namespace,
                    File = m.File,
                    Line = m.Line,
                    Cyclomatic = m.Cyclomatic,
                    Cognitive = m.Cognitive,
                    Loc = m.Loc,
                    Params = m.Params
                }).ToList(),
                TypeRollups = report.TypeRollups.Select(t => new TypeRollupItem
                {
                    Type = t.Type,
                    Namespace = t.Namespace,
                    MethodCount = t.MethodCount,
                    CyclomaticTotal = t.CyclomaticTotal,
                    CognitiveTotal = t.CognitiveTotal,
                    LocTotal = t.LocTotal,
                    ParamsTotal = t.ParamsTotal
                }).ToList(),
                NamespaceRollups = report.NamespaceRollups.Select(n => new NamespaceRollupItem
                {
                    Namespace = n.Namespace,
                    MethodCount = n.MethodCount,
                    CyclomaticTotal = n.CyclomaticTotal,
                    CognitiveTotal = n.CognitiveTotal,
                    LocTotal = n.LocTotal,
                    ParamsTotal = n.ParamsTotal
                }).ToList()
            };

            RtResultRenderer.Write(RtResult<ComplexityResult>.Ok(result));

            RtLog.Info("Analyzed {Count} method(s).", report.Methods.Count);
            return 0;
        }
    }

    private sealed record ComplexityCliRequest(
        string? SolutionPath,
        int Threshold,
        string Sort,
        int Top) : ISupportsSolutionOption, ISupportsTopOption
    {
        public static ComplexityCliRequest FromFlags(RtFlagValues flags, int threshold, string sort)
        {
            return new ComplexityCliRequest(flags.SolutionPath, threshold, sort, flags.Top);
        }
    }
}
