using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record DestaticClassRequest(
    [property: JsonRequired] string Type,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = true);

internal sealed record DestaticClassResult(string Type);

internal sealed class DestaticClassOperation
    : CommandApplicationPort<DestaticClassRequest, MutationExecutionResult<DestaticClassResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly DestaticClassService _service;
    private readonly IWorkspaceSessionFactory _sessions;

    public DestaticClassOperation(
        IWorkspaceSessionFactory sessions,
        DestaticClassService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => DestaticClassCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<DestaticClassResult>, RtError>> ExecuteAsync(
        DestaticClassRequest request,
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
                var value = new DestaticClassResult(prepared.TypeName);
                var changeSet = new PreparedChangeSet(solution, prepared.Solution);
                var execution = request.DryRun
                    ? _executor.Preview(value, changeSet)
                    : await _executor.ApplyAsync(
                        value,
                        changeSet,
                        cancellationToken: cancellationToken);
                return Result<MutationExecutionResult<DestaticClassResult>, RtError>.Ok(execution);
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<DestaticClassResult>, RtError>.Err(
                    new RtError(
                        RtErrorKind.InvalidInput,
                        exception.Message,
                        new Dictionary<string, string> { ["type"] = request.Type }));
            }
        }
    }
}

internal static class DestaticClassCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "convert-static-class",
        true,
        false,
        "refactor",
        Aliases: [],
        WhenToUse: "Use this instead of manual Edit when migrating a static class to an instance class with DI: drops the static modifier, "
                   + "generates a matching interface, registers in Bootstrap, and rewrites consumer call sites "
                   + "to use injected references. Pairs with rt analyze static-class-inventory for sequencing.",
        Examples:
        [
            "rt refactor convert-static-class RoslynTools.Services.InspectService --solution RoslynTools.slnx --dry-run",
            "rt refactor convert-static-class RoslynTools.Services.RenameService --no-interface --solution RoslynTools.slnx"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(DestaticClassRequest),
        typeof(MutationExecutionResult<DestaticClassResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        [],
        product: CommandProductMetadata.FromLegacy(
            Info,
            CommandSafety.ReviewByDefault,
            CommandAudience.Removed));
}
