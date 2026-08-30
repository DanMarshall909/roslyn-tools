using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for detecting and handling circular dependencies.
/// </summary>
internal sealed class CircularDepsCommand(
    DaemonRpcRecoveryCoordinator daemon,
    CircularDepsOperation operation) : IRtCommand, IAnalyzeCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly CircularDepsOperation _operation = operation;

    public static RtCommandInfo Info => CircularDepsCommandDefinition.Info;

    public Command Build()
    {
        var scopeOpt = new Option<string?>("--scope", "Optional namespace prefix to limit analysis");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");

        var cmd = new Command("circular-deps", "Detect circular namespace/type dependencies in the solution");
        cmd.AddAlias("cdeps");
        cmd.Add(scopeOpt);
        cmd.Add(solutionOpt);

        cmd.SetHandler(async context =>
        {
            var request = new CircularDepsRequest(
                context.ParseResult.GetValueForOption(solutionOpt),
                context.ParseResult.GetValueForOption(scopeOpt));
            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon, _operation, request, context.GetCancellationToken());
            result.Match(
                value => RtResultRenderer.Write(RtResult<RoslynTools.Commands.Results.CircularDepsResult>.Ok(
                    RoslynTools.Commands.Results.CircularDepsResult.FromCycles(
                        value.Cycles.Select(cycle => (IReadOnlyList<string>)cycle.Nodes).ToList()))),
                error => RtErrorPresenter.Surface(context, error));
        });

        return cmd;
    }
}
