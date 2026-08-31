using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record ReplaceDateTimeRequest(
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = true);

internal sealed record ReplaceDateTimeResult(int ChangedFiles, int ClassCount);

internal sealed class ReplaceDateTimeOperation
    : CommandApplicationPort<ReplaceDateTimeRequest,
        MutationExecutionResult<ReplaceDateTimeResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly ReplaceTypeService _service;

    public ReplaceDateTimeOperation(
        IWorkspaceSessionFactory sessions,
        ReplaceTypeService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => ReplaceDateTimeCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<ReplaceDateTimeResult>, RtError>> ExecuteAsync(
        ReplaceDateTimeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            var prepared = await _service.PrepareAsync(solution, cancellationToken);
            var value = new ReplaceDateTimeResult(prepared.ChangedFiles, prepared.ClassCount);
            if (prepared.ChangedFiles == 0)
                return Result<MutationExecutionResult<ReplaceDateTimeResult>, RtError>.Ok(
                    MutationExecutionResult<ReplaceDateTimeResult>.NoOp(
                        value,
                        [new MutationDiagnostic(
                            MutationDiagnosticSeverity.Information,
                            "No DateTime.UtcNow or DateTime.Now usage found.")]));

            var diagnostics = new MutationDiagnostic[]
            {
                new(
                    MutationDiagnosticSeverity.Warning,
                    "Manual review needed: verify constructor injection was added correctly in each class."),
                new(
                    MutationDiagnosticSeverity.Warning,
                    "Static classes and top-level statements need manual TimeProvider wiring.")
            };
            cancellationToken.ThrowIfCancellationRequested();
            var changeSet = new PreparedChangeSet(solution, prepared.Solution);
            var execution = request.DryRun
                ? _executor.Preview(value, changeSet, diagnostics)
                : await _executor.ApplyAsync(
                    value,
                    changeSet,
                    diagnostics,
                    cancellationToken);
            return Result<MutationExecutionResult<ReplaceDateTimeResult>, RtError>.Ok(execution);
        }
    }
}

internal static class ReplaceDateTimeCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "replace-datetime",
        true,
        false,
        "refactor",
        Aliases: null,
        WhenToUse:
        "Use this instead of manual Edit when replacing all DateTime.UtcNow and DateTime.Now usages with injected TimeProvider calls (_timeProvider.GetUtcNow().UtcDateTime / GetLocalNow().DateTime) and automatically wiring TimeProvider into the affected class constructors. Run solution-wide before adding unit tests that need controllable time.",
        Examples:
        [
            "rt refactor replace-datetime --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor replace-datetime --dry-run --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor replace-datetime --solution RoslynTools.slnx"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(ReplaceDateTimeRequest),
        typeof(MutationExecutionResult<ReplaceDateTimeResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        []);
}
