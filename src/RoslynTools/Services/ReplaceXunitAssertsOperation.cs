using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record ReplaceXunitAssertsRequest(
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = true,
    bool Apply = false,
    bool Review = false);

internal sealed record ReplaceXunitAssertsResult(
    int EntryCount,
    IReadOnlyList<ReplaceXunitAssertsEntry> Entries);

internal sealed class ReplaceXunitAssertsOperation
    : CommandApplicationPort<ReplaceXunitAssertsRequest,
        MutationExecutionResult<ReplaceXunitAssertsResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly ReplaceXunitAssertsService _service;

    public ReplaceXunitAssertsOperation(
        IWorkspaceSessionFactory sessions,
        ReplaceXunitAssertsService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition =>
        ReplaceXunitAssertsCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<ReplaceXunitAssertsResult>, RtError>> ExecuteAsync(
        ReplaceXunitAssertsRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            try
            {
                var review = await _service.AnalyzeAsync(solution, cancellationToken);
                var value = new ReplaceXunitAssertsResult(review.Entries.Count, review.Entries);

                if (request.Review || (!request.Apply && !request.DryRun))
                    return Result<MutationExecutionResult<ReplaceXunitAssertsResult>, RtError>.Ok(
                        MutationExecutionResult<ReplaceXunitAssertsResult>.Reviewed(value));

                var changedSolution = await _service.PrepareAsync(solution, cancellationToken);
                var changeSet = new PreparedChangeSet(solution, changedSolution);
                if (!changeSet.HasChanges)
                    return Result<MutationExecutionResult<ReplaceXunitAssertsResult>, RtError>.Ok(
                        MutationExecutionResult<ReplaceXunitAssertsResult>.NoOp(
                            value,
                            [new MutationDiagnostic(
                                MutationDiagnosticSeverity.Information,
                                "No supported Xunit assertions were found.")]));

                var execution = request.DryRun
                    ? _executor.Preview(value, changeSet)
                    : await _executor.ApplyAsync(
                        value,
                        changeSet,
                        cancellationToken: cancellationToken);
                return Result<MutationExecutionResult<ReplaceXunitAssertsResult>, RtError>.Ok(execution);
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<ReplaceXunitAssertsResult>, RtError>.Err(
                    new RtError(RtErrorKind.InvalidInput, exception.Message));
            }
        }
    }
}

internal static class ReplaceXunitAssertsCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "replace-xunit-asserts",
        true,
        false,
        "refactor",
        Aliases: ["rxa"],
        WhenToUse:
        "Use this instead of manual Edit calls when migrating Xunit Assert calls in tests to Shouldly's fluent assertion style. Defaults to review mode; pass --apply to write changes.",
        Examples:
        [
            "rt refactor replace-xunit-asserts --solution RoslynTools.slnx",
            "rt refactor rxa --apply --solution C:\\code\\app\\App.sln",
            "rt refactor replace-xunit-asserts --dry-run --solution C:\\code\\app\\App.sln"
        ],
        KnownIssues:
        [
            "Only common Assert calls are converted automatically; unsupported assertions are left unchanged.",
            "Assert overloads that include comparers, precision, inspectors, or custom messages are left unchanged.",
            "The command adds `using Shouldly;` to files with replacements and leaves existing Xunit imports in place for attributes such as [Fact]."
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(ReplaceXunitAssertsRequest),
        typeof(MutationExecutionResult<ReplaceXunitAssertsResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        []);
}
