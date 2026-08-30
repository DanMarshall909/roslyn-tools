using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record AddDynamicDependencyRequest(
    [property: JsonRequired] string[] Types,
    [property: JsonRequired] string Root,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = false);

internal sealed record AddDynamicDependencyResult(
    string Root,
    IReadOnlyList<string> Types,
    bool Changed);

internal sealed class AddDynamicDependencyOperation
    : CommandApplicationPort<AddDynamicDependencyRequest,
        MutationExecutionResult<AddDynamicDependencyResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly AddDynamicDependencyService _service;
    private readonly IWorkspaceSessionFactory _sessions;

    public AddDynamicDependencyOperation(
        IWorkspaceSessionFactory sessions,
        AddDynamicDependencyService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition =>
        AddDynamicDependencyCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<AddDynamicDependencyResult>, RtError>> ExecuteAsync(
        AddDynamicDependencyRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            try
            {
                var prepared = await _service.PrepareAsync(
                    solution,
                    request.Types,
                    request.Root);
                cancellationToken.ThrowIfCancellationRequested();
                var value = new AddDynamicDependencyResult(
                    request.Root,
                    request.Types,
                    prepared.Changed);
                if (!prepared.Changed)
                    return Result<MutationExecutionResult<AddDynamicDependencyResult>, RtError>.Ok(
                        MutationExecutionResult<AddDynamicDependencyResult>.NoOp(
                            value,
                            [new MutationDiagnostic(
                                MutationDiagnosticSeverity.Information,
                                "All requested dynamic dependencies already exist.")]));

                var changeSet = new PreparedChangeSet(solution, prepared.Solution);
                var execution = request.DryRun
                    ? _executor.Preview(value, changeSet)
                    : await _executor.ApplyAsync(
                        value,
                        changeSet,
                        cancellationToken: cancellationToken);
                return Result<MutationExecutionResult<AddDynamicDependencyResult>, RtError>.Ok(execution);
            }
            catch (ArgumentException exception)
            {
                return Result<MutationExecutionResult<AddDynamicDependencyResult>, RtError>.Err(
                    new RtError(RtErrorKind.InvalidInput, exception.Message));
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<AddDynamicDependencyResult>, RtError>.Err(
                    RtError.NotFound(exception.Message));
            }
        }
    }
}

internal static class AddDynamicDependencyCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "add-dynamic-dependency",
        true,
        false,
        "refactor",
        WhenToUse:
        "Recording a durable static reference for types instantiated through reflection or assembly-scanning dependency injection, after reviewing probe-di findings.",
        Examples:
        [
            "rt refactor add-dynamic-dependency --type MyApp.Services.EmailSender --root Bootstrap.RegisterServices --solution MyApp.slnx",
            "rt refactor add-dynamic-dependency --type ServiceA --type ServiceB --root Bootstrap.RegisterServices --dry-run"
        ],
        SeeAlso: ["probe-di", "find-unused"]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(AddDynamicDependencyRequest),
        typeof(MutationExecutionResult<AddDynamicDependencyResult>),
        true,
        CommandSafety.Mutation,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
