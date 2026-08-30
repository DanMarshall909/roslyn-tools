using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record MakeAsyncRequest(
    [property: JsonRequired] string Type,
    [property: JsonRequired] string Method,
    bool NoRename = false,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = false);

internal sealed record MakeAsyncResult(string Type, string Method);

internal sealed class MakeAsyncOperation
    : CommandApplicationPort<MakeAsyncRequest, MutationExecutionResult<MakeAsyncResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly MakeAsyncService _service;

    public MakeAsyncOperation(
        IWorkspaceSessionFactory sessions,
        MakeAsyncService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => MakeAsyncCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<MakeAsyncResult>, RtError>> ExecuteAsync(
        MakeAsyncRequest request,
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
                    request.Method,
                    request.NoRename,
                    cancellationToken);
                var value = new MakeAsyncResult(request.Type, prepared.MethodName);
                var changeSet = new PreparedChangeSet(solution, prepared.Solution);
                if (!changeSet.HasChanges)
                    return Result<MutationExecutionResult<MakeAsyncResult>, RtError>.Ok(
                        MutationExecutionResult<MakeAsyncResult>.NoOp(
                            value,
                            [new MutationDiagnostic(
                                MutationDiagnosticSeverity.Information,
                                "Method is already async.")]));

                cancellationToken.ThrowIfCancellationRequested();
                var execution = request.DryRun
                    ? _executor.Preview(value, changeSet)
                    : await _executor.ApplyAsync(
                        value,
                        changeSet,
                        cancellationToken: cancellationToken);
                return Result<MutationExecutionResult<MakeAsyncResult>, RtError>.Ok(execution);
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<MakeAsyncResult>, RtError>.Err(
                    new RtError(
                        RtErrorKind.InvalidInput,
                        exception.Message,
                        new Dictionary<string, string>
                        {
                            ["type"] = request.Type,
                            ["method"] = request.Method
                        }));
            }
        }
    }
}

internal static class MakeAsyncCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "make-async",
        true,
        false,
        "refactor",
        Aliases: ["ma"],
        WhenToUse:
        "Use this instead of manual Edit when converting a synchronous method to async Task<T> and updating its direct call sites with await, without manually hunting every invocation. Use --no-rename to skip the Async suffix when the call sites already have naming that makes async clear.",
        Examples:
        [
            "rt refactor make-async --type BookingService --method Save --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor ma --type EmailSender --method Send --no-rename --dry-run",
            "rt refactor make-async --type HeroApiClient --method FetchAvailability --solution RoslynTools.slnx"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(MakeAsyncRequest),
        typeof(MutationExecutionResult<MakeAsyncResult>),
        true,
        CommandSafety.Mutation,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
