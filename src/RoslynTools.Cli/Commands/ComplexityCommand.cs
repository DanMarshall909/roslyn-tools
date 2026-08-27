using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Computes code complexity metrics for a solution.
/// </summary>
internal sealed class ComplexityCommand(
    DaemonRpcRecoveryCoordinator daemon,
    ComplexityOperation operation) : IRtCommand, IStructuredOutputCommand, IAnalyzeCommand
{
    internal static readonly string[] ValidSortValues = ["cyclomatic", "cognitive", "loc", "params"];
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly ComplexityOperation _operation = operation;

    public static RtCommandInfo Info => ComplexityCommandDefinition.Definition.Info;

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
            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                new ComplexityRequest(request.SolutionPath, request.Threshold, request.Sort, request.Top),
                context.GetCancellationToken());
            result.Match(
                value => RtResultRenderer.Write(RtResult<Commands.Results.ComplexityResult>.Ok(
                    StaticAnalyzerResultAdapters.Complexity(value))),
                error => RtErrorPresenter.Surface(context, error));
        });

        return cmd;
    }

    /// <summary>
    /// Executes complexity analysis with optional parameters.
    /// </summary>
    internal async Task<int> ExecuteAsync(string? solution, int threshold, string sort, int top)
    {
        var result = await _operation.ExecuteAsync(new ComplexityRequest(solution, threshold, sort, top));
        return result.Match(value => { RtResultRenderer.Write(RtResult<RoslynTools.Commands.Results.ComplexityResult>.Ok(StaticAnalyzerResultAdapters.Complexity(value))); return 0; }, error => { RtLog.Error("{Message}", error.Message); return 1; });
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
