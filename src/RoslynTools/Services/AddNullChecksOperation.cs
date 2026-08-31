using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record AddNullChecksRequest(
    [property: JsonRequired] string Type,
    [property: JsonRequired] string Method,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = true);

internal sealed record AddNullChecksResult(string Type, string Method, bool Changed, string? Message);

internal sealed class AddNullChecksOperation
    : CommandApplicationPort<AddNullChecksRequest, MutationExecutionResult<AddNullChecksResult>>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly AddNullChecksService _service;
    private readonly PreparedChangeSetExecutor _executor;

    public AddNullChecksOperation(
        IWorkspaceSessionFactory sessions,
        AddNullChecksService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition { get; } = new(
        AddNullChecksCommandDefinition.Info,
        typeof(AddNullChecksRequest),
        typeof(MutationExecutionResult<AddNullChecksResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        []);

    public override async Task<Result<MutationExecutionResult<AddNullChecksResult>, RtError>> ExecuteAsync(
        AddNullChecksRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            try
            {
                var prepared = await _service.PrepareAsync(solution, request.Type, request.Method);
                cancellationToken.ThrowIfCancellationRequested();
                var value = new AddNullChecksResult(
                    request.Type,
                    request.Method,
                    prepared.Changed,
                    prepared.Message);
                if (!prepared.Changed)
                    return Result<MutationExecutionResult<AddNullChecksResult>, RtError>.Ok(
                        MutationExecutionResult<AddNullChecksResult>.NoOp(
                            value,
                            prepared.Message is null
                                ? []
                                : [new MutationDiagnostic(
                                    MutationDiagnosticSeverity.Information,
                                    prepared.Message)]));

                var changeSet = new PreparedChangeSet(solution, prepared.Solution);
                var execution = request.DryRun
                    ? _executor.Preview(value, changeSet)
                    : await _executor.ApplyAsync(value, changeSet, cancellationToken: cancellationToken);
                return Result<MutationExecutionResult<AddNullChecksResult>, RtError>.Ok(execution);
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<AddNullChecksResult>, RtError>.Err(
                    RtError.NotFound(exception.Message, new Dictionary<string, string>
                    {
                        ["type"] = request.Type,
                        ["method"] = request.Method
                    }));
            }
        }
    }
}

internal static class AddNullChecksCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "add-null-checks",
        true,
        false,
        "refactor",
        Aliases: ["anc"],
        WhenToUse:
        "Retrofitting ArgumentNullException.ThrowIfNull guards onto reference-type parameters in an existing method. Prefer this over a manual edit when a method has several parameters and you want to skip any that are already guarded or are nullable (annotated with ?).",
        Examples:
        [
            "rt refactor add-null-checks --type BookingService --method CreateBooking --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor anc --type OrderProcessor --method Process --dry-run",
            "rt refactor add-null-checks --type ReportController --method Generate --solution RoslynTools.slnx"
        ]);
}
