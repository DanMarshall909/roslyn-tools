using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record ConvertToRecordRequest(
    [property: JsonRequired] string Type,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = true);

internal sealed record ConvertToRecordResult(
    string Type,
    bool Changed);

internal sealed class ConvertToRecordOperation
    : CommandApplicationPort<ConvertToRecordRequest, MutationExecutionResult<ConvertToRecordResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly ConvertToRecordService _service;
    private readonly IWorkspaceSessionFactory _sessions;

    public ConvertToRecordOperation(
        IWorkspaceSessionFactory sessions,
        ConvertToRecordService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => ConvertToRecordCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<ConvertToRecordResult>, RtError>> ExecuteAsync(
        ConvertToRecordRequest request,
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
                    cancellationToken);
                var value = new ConvertToRecordResult(request.Type, prepared.Changed);
                if (!prepared.Changed)
                    return Result<MutationExecutionResult<ConvertToRecordResult>, RtError>.Ok(
                        MutationExecutionResult<ConvertToRecordResult>.NoOp(
                            value,
                            prepared.Diagnostics));

                var changeSet = new PreparedChangeSet(solution, prepared.Solution);
                var execution = request.DryRun
                    ? _executor.Preview(value, changeSet, prepared.Diagnostics)
                    : await _executor.ApplyAsync(
                        value,
                        changeSet,
                        prepared.Diagnostics,
                        cancellationToken);
                return Result<MutationExecutionResult<ConvertToRecordResult>, RtError>.Ok(execution);
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<ConvertToRecordResult>, RtError>.Err(
                    RtError.NotFound(exception.Message, new Dictionary<string, string>
                    {
                        ["type"] = request.Type
                    }));
            }
        }
    }
}

internal static class ConvertToRecordCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "convert-to-record",
        true,
        false,
        "refactor",
        Aliases: ["ctr"],
        WhenToUse:
        "Use this instead of manual Edit when converting a class with init-only properties to a positional record, automatically removing redundant Equals/GetHashCode overrides and == operators that the record synthesises. Best suited to immutable DTOs and value objects.",
        Examples:
        [
            "rt refactor convert-to-record --type BookingRequest --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor ctr --type Money --dry-run",
            "rt refactor convert-to-record --type ProductDto --solution RoslynTools.slnx"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(ConvertToRecordRequest),
        typeof(MutationExecutionResult<ConvertToRecordResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        []);
}
