using System.CommandLine;
using Microsoft.CodeAnalysis;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for detecting and handling circular dependencies.
/// </summary>
internal sealed class CircularDepsCommand : IRtCommand, IAnalyzeCommand
{
    private readonly CircularDepsOperation _operation;
    private readonly CircularDepsService _circularDeps;

    public CircularDepsCommand(
        CircularDepsOperation operation,
        CircularDepsService circularDeps)
    {
        _operation = operation;
        _circularDeps = circularDeps;
    }

    public static RtCommandInfo Info => CircularDepsCommandDefinition.Info;

    public Command Build()
    {
        var scopeOpt = new Option<string?>("--scope", "Optional namespace prefix to limit analysis");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");

        var cmd = new Command("circular-deps", "Detect circular namespace/type dependencies in the solution");
        cmd.AddAlias("cdeps");
        cmd.Add(scopeOpt);
        cmd.Add(solutionOpt);

        cmd.SetHandler(async (scope, solution) =>
        {
            var analysis = await _operation.ExecuteAsync(new CircularDepsRequest(solution, scope));
            var result = CircularDepsResult.FromCycles(
                analysis.Cycles.Select(cycle => cycle.Nodes).ToList());
            RtResultRenderer.Write(RtResult<CircularDepsResult>.Ok(result));
        }, scopeOpt, solutionOpt);

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
