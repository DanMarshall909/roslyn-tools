using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;
using RoslynTools.Services.Results;

namespace RoslynTools.Commands;

/// <summary>
/// Lists all smells detected in the current file
/// </summary>
internal sealed class ListSmellsCommand(
    DaemonRpcRecoveryCoordinator daemon,
    ListSmellsOperation operation) : IRtCommand, IAnalyzeCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly ListSmellsOperation _operation = operation;

    public static RtCommandInfo Info => ListSmellsCommandDefinition.Definition.Info;

    public Command Build()
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

            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                new ListSmellsRequest(
                    request.SolutionPath,
                    request.Scope,
                    request.Top,
                    request.MaxClassLines,
                    request.MaxMethodLines,
                    request.MaxParams),
                ctx.GetCancellationToken());
            result.Match(
                value => RtResultRenderer.Write(RtResult<Commands.Results.ListSmellsResult>.Ok(
                    StaticAnalyzerResultAdapters.ListSmells(value))),
                error => RtErrorPresenter.Surface(ctx, error));
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
