using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record MakeStaticRequest(
    [property: JsonRequired] string Method,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = true);

internal sealed record MakeStaticResult(string Method, bool Changed);

internal sealed class MakeStaticOperation
    : CommandApplicationPort<MakeStaticRequest, MutationExecutionResult<MakeStaticResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly MakeStaticService _service;

    public MakeStaticOperation(
        IWorkspaceSessionFactory sessions,
        MakeStaticService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => MakeStaticCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<MakeStaticResult>, RtError>> ExecuteAsync(
        MakeStaticRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var prepared = await _service.PrepareAsync(solution, request.Method);
                cancellationToken.ThrowIfCancellationRequested();
                var value = new MakeStaticResult(request.Method, prepared.Changed);
                var changes = new PreparedChangeSet(solution, prepared.Solution);
                var execution = request.DryRun
                    ? _executor.Preview(value, changes)
                    : await _executor.ApplyAsync(
                        value,
                        changes,
                        cancellationToken: cancellationToken);
                return Result<MutationExecutionResult<MakeStaticResult>, RtError>.Ok(execution);
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<MakeStaticResult>, RtError>.Err(
                    new RtError(RtErrorKind.InvalidInput, exception.Message));
            }
        }
    }
}

internal static class MakeStaticCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "make-static",
        true,
        false,
        "refactor",
        Aliases: ["mst"],
        WhenToUse: "Use this instead of manual Edit when converting an instance method to static when it has no instance-state dependencies. "
                   + "Validates safety (no 'this', no instance field/property access, not virtual/override/abstract) "
                   + "before modifying and rewrites all call sites from instance.Method() to Type.Method().",
        Examples:
        [
            "rt refactor make-static --method OrderValidator.Validate --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor mst --method MathHelper.Clamp --dry-run",
            "rt refactor make-static --method SymbolLocator.ParseSpec --solution RoslynTools.slnx"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(MakeStaticRequest),
        typeof(MutationExecutionResult<MakeStaticResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        []);
}
