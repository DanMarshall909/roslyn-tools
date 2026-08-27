using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// A command for retrieving static class inventory information.
/// </summary>
internal sealed class StaticClassInventoryCommand(
    DaemonRpcRecoveryCoordinator daemon,
    StaticClassInventoryOperation operation) : IRtCommand, IStructuredOutputCommand, IAnalyzeCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly StaticClassInventoryOperation _operation = operation;
    public static RtCommandInfo Info => StaticClassInventoryCommandDefinition.Info;
    public Command Build()
    {
        var binder = RtCommandFlagBinder.Create()
            .WithSolution("Path to the .sln/.slnx file");

        var cmd = new Command("static-class-inventory",
            "List every static class with call-site count, member count, and inbound coupling for migration planning");
        binder.AddTo(cmd);

        cmd.SetHandler(async ctx =>
        {
            var req = binder.Bind(ctx.ParseResult, f => new StaticClassInventoryCliRequest(f.SolutionPath));

            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                new StaticClassInventoryRequest(req.SolutionPath),
                ctx.GetCancellationToken());
            result.Match(
                value => RtResultRenderer.Write(RtResult<StaticClassInventoryResult>.Ok(
                    StaticClassInventoryResult.FromAnalysis(value))),
                error => RtErrorPresenter.Surface(ctx, error));
        });

        return cmd;
    }

    private sealed record StaticClassInventoryCliRequest(string? SolutionPath) : ISupportsSolutionOption;
}
