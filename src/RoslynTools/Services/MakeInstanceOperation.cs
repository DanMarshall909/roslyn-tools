using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record MakeInstanceRequest(
    [property: JsonRequired] string Method,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = true);

internal sealed record MakeInstanceResult(
    string Method,
    int CallSites,
    int RewrittenCallSites,
    int SkippedCallSites);

internal sealed class MakeInstanceOperation
    : CommandApplicationPort<MakeInstanceRequest, MutationExecutionResult<MakeInstanceResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly MakeInstanceService _service;

    public MakeInstanceOperation(
        IWorkspaceSessionFactory sessions,
        MakeInstanceService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => MakeInstanceCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<MakeInstanceResult>, RtError>> ExecuteAsync(
        MakeInstanceRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            try
            {
                var prepared = await _service.PrepareAsync(solution, request.Method, cancellationToken);
                var diagnostics = prepared.SkippedCallSites
                    .Select(skipped => new MutationDiagnostic(
                        MutationDiagnosticSeverity.Warning,
                        $"{skipped.File}:{skipped.Line}: {skipped.Reason} — skipped",
                        Context: new Dictionary<string, string>
                        {
                            ["file"] = skipped.File,
                            ["line"] = skipped.Line.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["reason"] = skipped.Reason
                        }))
                    .ToArray();
                var skippedCount = prepared.SkippedCallSites.Count;
                var value = new MakeInstanceResult(
                    request.Method,
                    prepared.TotalCallSites,
                    prepared.TotalCallSites - skippedCount,
                    skippedCount);

                cancellationToken.ThrowIfCancellationRequested();
                var changeSet = new PreparedChangeSet(solution, prepared.Solution);
                var execution = request.DryRun
                    ? _executor.Preview(value, changeSet, diagnostics)
                    : await _executor.ApplyAsync(
                        value,
                        changeSet,
                        diagnostics,
                        cancellationToken);
                return Result<MutationExecutionResult<MakeInstanceResult>, RtError>.Ok(execution);
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<MakeInstanceResult>, RtError>.Err(
                    new RtError(
                        RtErrorKind.InvalidInput,
                        exception.Message,
                        new Dictionary<string, string> { ["method"] = request.Method }));
            }
        }
    }
}

internal static class MakeInstanceCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "make-instance",
        true,
        false,
        "refactor",
        Aliases: ["mns"],
        WhenToUse:
        "Use this instead of manual Edit when converting a static method to an instance method. Removes the `static` modifier from the declaration and rewrites `Type.Method(...)` call sites to `instance.Method(...)` where an instance is reachable (local variable, field, or parameter of the correct type). Call sites with no reachable instance are warned and left unchanged.",
        Examples:
        [
            "rt refactor make-instance --method OrderService.Validate --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor mns --method MathHelper.Compute --dry-run --solution RoslynTools.slnx"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(MakeInstanceRequest),
        typeof(MutationExecutionResult<MakeInstanceResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        []);
}
