using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record ReplaceCallRequest(
    [property: JsonRequired] string From,
    [property: JsonRequired] string To,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = false,
    [property: JsonPropertyName("if-arg")] int? IfArgIndex = null,
    [property: JsonPropertyName("if-arg-literal")] string? IfArgLiteral = null);

internal sealed record ReplaceCallResult(string From, string To, bool Changed, string? Message);

internal sealed class ReplaceCallOperation
    : CommandApplicationPort<ReplaceCallRequest, MutationExecutionResult<ReplaceCallResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly ReplaceCallService _service;

    public ReplaceCallOperation(
        IWorkspaceSessionFactory sessions,
        ReplaceCallService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => ReplaceCallCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<ReplaceCallResult>, RtError>> ExecuteAsync(
        ReplaceCallRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            try
            {
                var prepared = await _service.PrepareAsync(solution, request.From, request.To, request.IfArgIndex, request.IfArgLiteral);
                cancellationToken.ThrowIfCancellationRequested();
                var value = new ReplaceCallResult(request.From, request.To, prepared.Changed, prepared.Message);
                if (!prepared.Changed)
                    return Result<MutationExecutionResult<ReplaceCallResult>, RtError>.Ok(
                        MutationExecutionResult<ReplaceCallResult>.NoOp(
                            value,
                            prepared.Message is null
                                ? []
                                : [new MutationDiagnostic(
                                    MutationDiagnosticSeverity.Information,
                                    prepared.Message)]));
                var set = new PreparedChangeSet(solution, prepared.Solution);
                var execution = request.DryRun ? _executor.Preview(value, set) : await _executor.ApplyAsync(value, set, cancellationToken: cancellationToken);
                return Result<MutationExecutionResult<ReplaceCallResult>, RtError>.Ok(execution);
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<ReplaceCallResult>, RtError>.Err(new RtError(RtErrorKind.InvalidInput, exception.Message));
            }
        }
    }
}

internal static class ReplaceCallCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "replace-call",
        true,
        false,
        "refactor",
        Aliases: ["rc"],
        WhenToUse:
        "Use this instead of manual Edit when replacing all invocations of a static method with a different method name across the solution, especially when renaming a utility method that isn't captured by a semantic rename (e.g. because callers use it via a static import or the old name needs to stay as an overload).",
        Examples:
        [
            "rt refactor replace-call --from Logger.Log --to LogInfo --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor rc --from DateHelper.UtcNow --to TimeProvider.GetUtcNow --dry-run --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor replace-call --from Console.WriteLine --to Logger.Info --solution RoslynTools.slnx"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(ReplaceCallRequest),
        typeof(MutationExecutionResult<ReplaceCallResult>),
        true,
        CommandSafety.Mutation,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
