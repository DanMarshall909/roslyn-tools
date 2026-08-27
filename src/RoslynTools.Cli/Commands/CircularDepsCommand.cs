using System.CommandLine;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for detecting and handling circular dependencies.
/// </summary>
internal sealed class CircularDepsCommand(
    DaemonRpcClient daemon,
    CircularDepsService circularDeps) : IRtCommand, IAnalyzeCommand
{
    private readonly DaemonRpcClient _daemon = daemon;
    private readonly CircularDepsService _circularDeps = circularDeps;

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
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                CircularDepsCommandDefinition.Name,
                request,
                context);
        });

        return cmd;
    }

    /// <summary>
    /// Analyzes cycles in a solution using Roslyn.
    /// </summary>
    internal Task<IReadOnlyList<IReadOnlyList<string>>> AnalyzeCyclesAsync(Solution sln, string? scope)
    {
        return _circularDeps.AnalyzeCyclesAsync(sln, scope);
    }

    /// <summary>
    /// Analyzes cycles in a solution asynchronously.
    /// </summary>
    internal Task<string> AnalyzeAsync(Solution sln, string? scope)
    {
        return _circularDeps.AnalyzeAsync(sln, scope);
    }
}
