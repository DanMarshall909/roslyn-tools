using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record RemoveUnusedUsingsRequest(
    string? File = null,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = true);

internal sealed record RemoveUnusedUsingsResult(
    int RemovedCount,
    IReadOnlyList<RemovedUsingDirective> RemovedUsings);

internal sealed class RemoveUnusedUsingsOperation
    : CommandApplicationPort<RemoveUnusedUsingsRequest, MutationExecutionResult<RemoveUnusedUsingsResult>>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly RemoveUnusedUsingsService _service;
    private readonly PreparedChangeSetExecutor _executor;

    public RemoveUnusedUsingsOperation(
        IWorkspaceSessionFactory sessions,
        RemoveUnusedUsingsService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => RemoveUnusedUsingsCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<RemoveUnusedUsingsResult>, RtError>> ExecuteAsync(
        RemoveUnusedUsingsRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            try
            {
                var preparation = await _service.PrepareAsync(
                    solution,
                    request.File,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                var value = new RemoveUnusedUsingsResult(
                    preparation.RemovedUsings.Count,
                    preparation.RemovedUsings);
                if (preparation.RemovedUsings.Count == 0)
                    return Result<MutationExecutionResult<RemoveUnusedUsingsResult>, RtError>.Ok(
                        MutationExecutionResult<RemoveUnusedUsingsResult>.NoOp(
                            value,
                            [new MutationDiagnostic(
                                MutationDiagnosticSeverity.Information,
                                "No unused using directives were found.")]));

                var changeSet = new PreparedChangeSet(solution, preparation.Solution);
                var execution = request.DryRun
                    ? _executor.Preview(value, changeSet)
                    : await _executor.ApplyAsync(value, changeSet, cancellationToken: cancellationToken);
                return Result<MutationExecutionResult<RemoveUnusedUsingsResult>, RtError>.Ok(execution);
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<RemoveUnusedUsingsResult>, RtError>.Err(
                    RtError.NotFound(
                        exception.Message,
                        request.File is null
                            ? null
                            : new Dictionary<string, string> { ["file"] = request.File }));
            }
            catch (ArgumentException exception)
            {
                return Result<MutationExecutionResult<RemoveUnusedUsingsResult>, RtError>.Err(
                    new RtError(
                        RtErrorKind.InvalidInput,
                        exception.Message,
                        request.File is null
                            ? null
                            : new Dictionary<string, string> { ["file"] = request.File }));
            }
        }
    }
}

internal static class RemoveUnusedUsingsCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "remove-unused-usings",
        true,
        false,
        "refactor",
        Aliases: ["remove-unused-imports"],
        WhenToUse:
        "Use this instead of manual Edit when C# files contain unused using directives after moving or deleting code. Omit the file argument to review all compiler-confirmed unnecessary usings in the solution.",
        Examples:
        [
            "rt refactor remove-unused-usings src/Services/BookingService.cs --solution RoslynTools.slnx",
            "rt refactor remove-unused-usings --solution RoslynTools.slnx",
            "rt refactor remove-unused-imports src/Program.cs --apply --solution C:\\code\\app\\App.slnx"
        ],
        SeeAlso: ["list-imports"]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(RemoveUnusedUsingsRequest),
        typeof(MutationExecutionResult<RemoveUnusedUsingsResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        []);
}
