using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Identifies unused commands in the current file
/// </summary>
internal sealed class FindUnusedCommand(
    DaemonRpcRecoveryCoordinator daemon,
    FindUnusedOperation operation) : IRtCommand, IInspectCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly FindUnusedOperation _operation = operation;
    public static RtCommandInfo Info => FindUnusedCommandDefinition.Info;
    public Command Build()
    {
        var binder = RtCommandFlagBinder.Create()
            .WithSolution("Path to .sln/.slnx");
        var scopeOpt = new Option<string?>("--scope", "Filter: types, methods, or all (default: all)");

        var cmd = new Command("find-unused", "Report types and methods with no references (dead code)");
        cmd.AddAlias("unused");
        cmd.Add(scopeOpt);
        binder.AddTo(cmd);

        cmd.SetHandler(async ctx =>
        {
            var scope = ctx.ParseResult.GetValueForOption(scopeOpt);
            var req = binder.Bind(ctx.ParseResult, f => new FindUnusedCliRequest(f.SolutionPath, scope));

            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                new FindUnusedRequest(req.SolutionPath, req.Scope ?? "all"),
                ctx.GetCancellationToken());
            result.Match(
                value => RtResultRenderer.Write(RtResult<FindUnusedResult>.Ok(
                    FindUnusedResult.FromAnalysis(value))),
                error => RtErrorPresenter.Surface(ctx, error));
        });

        return cmd;
    }

    private sealed record FindUnusedCliRequest(string? SolutionPath, string? Scope) : ISupportsSolutionOption;
}
