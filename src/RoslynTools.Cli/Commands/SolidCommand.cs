using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Commands.Results;
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
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var principleOpt = new Option<string>("--principle", () => "SRP",
            "SOLID principle to check: SRP or all");
        var minMethodsOpt = new Option<int>("--min-methods", () => 10,
            "Minimum public method count to flag a class (default 10)");
        var minDepsOpt = new Option<int>("--min-deps", () => 5,
            "Minimum injected dependency count to flag a class (default 5)");

        var cmd = new Command("solid", "Detect SOLID principle violations using heuristic analysis");
        cmd.AddAll(solutionOpt, principleOpt, minMethodsOpt, minDepsOpt);

        cmd.SetHandler(async context =>
        {
            var sp = context.BindingContext.GetService<IServiceProvider>();
            var loader = sp?.GetService<WorkspaceLoader>() ?? new WorkspaceLoader();
            context.ExitCode = await ExecuteAsync(
                context.ParseResult.GetValueForOption(solutionOpt),
                context.ParseResult.GetValueForOption(principleOpt)!,
                context.ParseResult.GetValueForOption(minMethodsOpt),
                context.ParseResult.GetValueForOption(minDepsOpt),
                loader);
        });

        return cmd;
    }

    internal static async Task<int> ExecuteAsync(
        string? solution, string principle, int minMethods, int minDeps,
        WorkspaceLoader? loader = null)
    {
        if (minMethods < 0)
        {
            RtLog.Error("--min-methods must be >= 0.");
            return 1;
        }

        if (minDeps < 0)
        {
            RtLog.Error("--min-deps must be >= 0.");
            return 1;
        }

        var normalised = principle.ToUpperInvariant();
        if (normalised is not ("SRP" or "ALL"))
        {
            RtLog.Error("--principle must be SRP or all.");
            return 1;
        }

        var effectiveLoader = loader ?? new WorkspaceLoader();
        var sln = await effectiveLoader.GetSolutionAsync(solution);
        RtLog.Info("Analyzing SOLID principles...");
        var srpReport = await SolidAnalyzer.AnalyzeSrpAsync(sln, minMethods, minDeps);
        var result = new SolidResult
        {
            SRP = srpReport.Violations.Select(v => new SrpViolationItem
            {
                TypeName = v.TypeName,
                FilePath = v.FilePath,
                Line = v.Line,
                PublicMethodCount = v.PublicMethodCount,
                DependencyCount = v.DependencyCount,
                Detail = v.Detail
            }).ToList()
        };
        RtResultRenderer.WriteOutput(RtResult<SolidResult>.Ok(result));
        RtLog.Info("Found {Count} SRP violation(s).", result.SRP.Count);
        return 0;
    }
}
