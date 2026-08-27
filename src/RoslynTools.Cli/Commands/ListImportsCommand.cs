using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Cli;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Lists all imported namespaces in the current file
/// </summary>
internal sealed class ListImportsCommand(
    DaemonRpcRecoveryCoordinator daemon,
    ListImportsOperation operation) : IRtCommand, IAnalyzeCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly ListImportsOperation _operation = operation;

    public static RtCommandInfo Info => ListImportsCommandDefinition.Info;

    public Command Build()
    {
        var binder = RtCommandFlagBinder.Create()
            .WithSolution("Path to .sln/.slnx");
        var kindOpt = new Option<string>("--kind", () => "all", "Filter: all, global, static, or alias");

        var cmd = new Command("list-imports", "List using directives, including global and static imports");
        cmd.AddAlias("imports");
        cmd.Add(kindOpt);
        binder.AddTo(cmd);

        cmd.SetHandler(async ctx =>
        {
            var kind = ctx.ParseResult.GetValueForOption(kindOpt);
            var req = binder.Bind(ctx.ParseResult, f => new ListImportsCliRequest(f.SolutionPath, kind!));
            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                new ListImportsRequest(req.SolutionPath, req.ImportKind),
                ctx.GetCancellationToken());
            result.Match(
                value => RtResultRenderer.Write(RtResult<ListImportsResult>.Ok(value)),
                error => RtErrorPresenter.Surface(ctx, error));
        });

        return cmd;
    }

    private sealed record ListImportsCliRequest(string? SolutionPath, string ImportKind) : ISupportsSolutionOption;
}
