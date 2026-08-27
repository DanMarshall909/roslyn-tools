using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record ImplementInterfaceRequest(
    [property: JsonRequired] string Type,
    string? Interface = null,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = false);

internal sealed record ImplementInterfaceResult(string Type, int StubCount);

internal sealed class ImplementInterfaceOperation
    : CommandApplicationPort<ImplementInterfaceRequest,
        MutationExecutionResult<ImplementInterfaceResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly ImplementInterfaceService _service;

    public ImplementInterfaceOperation(
        IWorkspaceSessionFactory sessions,
        ImplementInterfaceService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => ImplementInterfaceCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<ImplementInterfaceResult>, RtError>> ExecuteAsync(
        ImplementInterfaceRequest request,
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
                    request.Type,
                    request.Interface,
                    cancellationToken);
                var value = new ImplementInterfaceResult(request.Type, prepared.StubCount);
                if (prepared.Message is not null)
                    return Result<MutationExecutionResult<ImplementInterfaceResult>, RtError>.Ok(
                        MutationExecutionResult<ImplementInterfaceResult>.NoOp(
                            value,
                            [new MutationDiagnostic(
                                MutationDiagnosticSeverity.Information,
                                prepared.Message)]));

                cancellationToken.ThrowIfCancellationRequested();
                var changeSet = new PreparedChangeSet(solution, prepared.Solution);
                var execution = request.DryRun
                    ? _executor.Preview(value, changeSet)
                    : await _executor.ApplyAsync(
                        value,
                        changeSet,
                        cancellationToken: cancellationToken);
                return Result<MutationExecutionResult<ImplementInterfaceResult>, RtError>.Ok(execution);
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<ImplementInterfaceResult>, RtError>.Err(
                    new RtError(RtErrorKind.InvalidInput, exception.Message));
            }
        }
    }
}

internal static class ImplementInterfaceCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "implement-interface",
        true,
        false,
        "refactor",
        Aliases: ["ii"],
        WhenToUse:
        "Generating throw-stub implementations for all unimplemented interface members in a class. Use after extract-interface or when a class is added to an interface's base list and needs skeleton stubs before real implementations are written.",
        Examples:
        [
            "rt refactor implement-interface --type BookingRepository --interface IBookingRepository --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor ii --type EmailSender --dry-run",
            "rt refactor implement-interface --type HeroApiClient --solution RoslynTools.slnx"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(ImplementInterfaceRequest),
        typeof(MutationExecutionResult<ImplementInterfaceResult>),
        true,
        CommandSafety.Mutation,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
