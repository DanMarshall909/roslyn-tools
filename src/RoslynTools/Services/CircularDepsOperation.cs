using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for circular namespace dependency analysis.</summary>
internal sealed record CircularDepsRequest(
    string? Solution = null,
    string? Scope = null);

/// <summary>One detected namespace dependency cycle.</summary>
internal sealed record CircularDependencyCycle(IReadOnlyList<string> Nodes)
{
    public string Path => string.Join(" -> ", Nodes);
}

/// <summary>
///     Structured circular-dependency analysis result, produced before a host selects its
///     presentation format.
/// </summary>
internal sealed record CircularDepsAnalysisResult(IReadOnlyList<CircularDependencyCycle> Cycles)
{
    public bool HasCycles => Cycles.Count > 0;
}

/// <summary>
///     Shared circular-dependency analysis operation. It owns workspace lifetime so CLI,
///     daemon, and MCP adapters use the same solution-loading and scope behavior.
/// </summary>
internal sealed class CircularDepsOperation
    : CommandApplicationPort<CircularDepsRequest, CircularDepsAnalysisResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly CircularDepsService _circularDeps;

    public CircularDepsOperation(
        IWorkspaceSessionFactory sessionFactory,
        CircularDepsService circularDeps)
    {
        _sessionFactory = sessionFactory;
        _circularDeps = circularDeps;
    }

    public override ApplicationCommandDefinition Definition => new(
        CircularDepsCommandDefinition.Info,
        typeof(CircularDepsRequest),
        typeof(CircularDepsAnalysisResult),
        true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        []);

    public override async Task<Result<CircularDepsAnalysisResult, RtError>> ExecuteAsync(
        CircularDepsRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cycles = await _circularDeps.AnalyzeCyclesAsync(solution, request.Scope);
            return Result<CircularDepsAnalysisResult, RtError>.Ok(new CircularDepsAnalysisResult(
                cycles.Select(nodes => new CircularDependencyCycle(nodes)).ToList()));
        }
    }
}
