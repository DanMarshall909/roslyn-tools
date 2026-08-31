using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record InlineMethodRequest(
    [property: JsonRequired] string Type,
    [property: JsonRequired] string Method,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = true);

internal sealed record InlineMethodResult(string Type, string Method, bool Changed);

internal sealed class InlineMethodOperation
    : CommandApplicationPort<InlineMethodRequest, MutationExecutionResult<InlineMethodResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly InlineMethodService _service;

    public InlineMethodOperation(
        IWorkspaceSessionFactory sessions,
        InlineMethodService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => InlineMethodCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<InlineMethodResult>, RtError>> ExecuteAsync(
        InlineMethodRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            var prepared = await _service.PrepareAsync(solution, request.Type, request.Method);
            cancellationToken.ThrowIfCancellationRequested();
            if (prepared.Error is not null)
                return Result<MutationExecutionResult<InlineMethodResult>, RtError>.Err(
                    new RtError(RtErrorKind.InvalidInput, prepared.Error));
            var value = new InlineMethodResult(request.Type, request.Method, prepared.Changed);
            if (!prepared.Changed)
                return Result<MutationExecutionResult<InlineMethodResult>, RtError>.Ok(
                    MutationExecutionResult<InlineMethodResult>.NoOp(value, []));
            var set = new PreparedChangeSet(solution, prepared.Solution);
            var execution = request.DryRun
                ? _executor.Preview(value, set)
                : await _executor.ApplyAsync(value, set, cancellationToken: cancellationToken);
            return Result<MutationExecutionResult<InlineMethodResult>, RtError>.Ok(execution);
        }
    }
}

internal static class InlineMethodCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "inline-method",
        true,
        false,
        "refactor",
        Aliases: ["im"],
        WhenToUse:
        "Use this instead of manual Edit when inlining a trivial helper method (expression-bodied or single-return block) at every call site and removing the method declaration. Best for one-liner wrappers that add no clarity — over-extracted private methods that obscure intent.",
        Examples:
        [
            "rt refactor inline-method --type MathHelper --method Double --solution RoslynTools.slnx",
            "rt refactor im --type OrderService --method GetTotal --dry-run --solution C:\\code\\libraries\\ProjectX.Libraries.sln"
        ],
        SeeAlso: ["extract-method", "relationships"]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(InlineMethodRequest),
        typeof(MutationExecutionResult<InlineMethodResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        []);
}
