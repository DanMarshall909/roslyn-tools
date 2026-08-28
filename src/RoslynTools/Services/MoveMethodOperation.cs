using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record MoveMethodRequest(
    [property: JsonRequired]
    [property: JsonPropertyName("method-fqn")]
    [property: CommandPropertyDescription("Fully-qualified static method to move")]
    string MethodFqn,
    [property: JsonRequired]
    [property: JsonPropertyName("to-type")]
    [property: CommandPropertyDescription("Fully-qualified target class")]
    string ToType,
    [property: JsonPropertyName("create-file")] bool CreateFile = false,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = false);

internal sealed record MoveMethodResult(
    string MethodFqn,
    string ToType,
    string MethodName,
    int CallSiteCount,
    bool CreatedTargetFile,
    string? CreatedTargetFileName);

/// <summary>Shared static-method move operation used by CLI, daemon, and MCP.</summary>
internal sealed class MoveMethodOperation
    : CommandApplicationPort<MoveMethodRequest, MutationExecutionResult<MoveMethodResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly MoveMethodService _service;

    public MoveMethodOperation(
        IWorkspaceSessionFactory sessions,
        MoveMethodService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => MoveMethodCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<MoveMethodResult>, RtError>> ExecuteAsync(
        MoveMethodRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.MethodFqn))
            return Result<MutationExecutionResult<MoveMethodResult>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "A method-fqn is required.",
                new Dictionary<string, string> { ["method-fqn"] = request.MethodFqn }));

        if (string.IsNullOrWhiteSpace(request.ToType))
            return Result<MutationExecutionResult<MoveMethodResult>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "A to-type is required.",
                new Dictionary<string, string> { ["to-type"] = request.ToType }));

        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            var preparationResult = await _service.PrepareAsync(
                solution,
                request.MethodFqn,
                request.ToType,
                request.CreateFile,
                cancellationToken);
            MoveMethodService.Preparation? preparation = null;
            RtError? error = null;
            preparationResult.Match(value => preparation = value, failure => error = failure);
            if (error is not null)
                return Result<MutationExecutionResult<MoveMethodResult>, RtError>.Err(error);

            cancellationToken.ThrowIfCancellationRequested();
            var value = new MoveMethodResult(
                preparation!.MethodFqn,
                preparation.TargetTypeFqn,
                preparation.MethodName,
                preparation.CallSiteCount,
                preparation.CreatedTargetFile,
                preparation.CreatedTargetFileName);
            var changeSet = new PreparedChangeSet(solution, preparation.Solution);
            var execution = request.DryRun
                ? _executor.Preview(value, changeSet)
                : await _executor.ApplyAsync(value, changeSet, cancellationToken: cancellationToken);
            return Result<MutationExecutionResult<MoveMethodResult>, RtError>.Ok(execution);
        }
    }
}

internal static class MoveMethodCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "move-method",
        true,
        false,
        "refactor",
        WhenToUse:
        "Use this instead of manual Edit when moving a static method between classes and rewriting every bound call site in the solution. Pass --create-file only when the target class should be created beside the source file.",
        Examples:
        [
            "rt refactor move-method App.Source.Add --to-type App.Target --solution RoslynTools.slnx --dry-run",
            "rt refactor move-method MathApp.MathHelper.Double --to-type MathApp.MathUtils --solution MySolution.sln",
            "rt refactor move-method Helpers.StringUtils.Truncate --to-type Helpers.TextHelper --create-file --solution MySolution.sln"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(MoveMethodRequest),
        typeof(MutationExecutionResult<MoveMethodResult>),
        true,
        CommandSafety.Mutation,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
