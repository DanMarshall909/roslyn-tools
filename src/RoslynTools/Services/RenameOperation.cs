using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Models;

namespace RoslynTools.Services;

[JsonConverter(typeof(JsonStringEnumConverter<RenameOperationMode>))]
internal enum RenameOperationMode
{
    Single,
    Batch
}

internal sealed record RenameOperationRequest(
    [property: JsonRequired] RenameOperationMode Mode,
    string? Symbol = null,
    [property: JsonPropertyName("new-name")] string? NewName = null,
    [property: JsonPropertyName("csv")] string? CsvPath = null,
    string? Kind = null,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = false,
    [property: JsonPropertyName("related-edits")] string[]? RelatedEdits = null,
    [property: JsonPropertyName("working-directory")] string? WorkingDirectory = null);

internal sealed record RenameOperationResult(
    RenameOperationMode Mode,
    IReadOnlyList<RenameResult> Renames,
    int Skipped);

/// <summary>Shared semantic rename operation used by CLI and MCP.</summary>
internal sealed class RenameOperation
    : CommandApplicationPort<RenameOperationRequest, MutationExecutionResult<RenameOperationResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly RenameService _rename;
    private readonly IWorkspaceSessionFactory _sessions;

    public RenameOperation(
        IWorkspaceSessionFactory sessions,
        RenameService rename,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _rename = rename;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => RenameCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<RenameOperationResult>, RtError>> ExecuteAsync(
        RenameOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (Validate(request) is { } validationError)
            return Result<MutationExecutionResult<RenameOperationResult>, RtError>.Err(validationError);

        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            try
            {
                return request.Mode switch
                {
                    RenameOperationMode.Single => await ExecuteSingleAsync(
                        solution,
                        request,
                        cancellationToken),
                    RenameOperationMode.Batch => await ExecuteBatchAsync(
                        solution,
                        request,
                        cancellationToken),
                    _ => Result<MutationExecutionResult<RenameOperationResult>, RtError>.Err(
                        new RtError(RtErrorKind.InvalidInput, $"Unsupported rename mode '{request.Mode}'."))
                };
            }
            catch (FileNotFoundException exception)
            {
                return Result<MutationExecutionResult<RenameOperationResult>, RtError>.Err(
                    RtError.NotFound(exception.Message, new Dictionary<string, string>
                    {
                        ["path"] = exception.FileName ?? request.CsvPath ?? string.Empty
                    }));
            }
            catch (IOException exception)
            {
                return Result<MutationExecutionResult<RenameOperationResult>, RtError>.Err(new RtError(
                    RtErrorKind.InvalidInput,
                    exception.Message));
            }
            catch (InvalidOperationException exception)
            {
                var error = exception.Message.StartsWith("No symbol with FQN", StringComparison.Ordinal)
                    ? RtError.NotFound(exception.Message)
                    : new RtError(RtErrorKind.InvalidInput, exception.Message);
                return Result<MutationExecutionResult<RenameOperationResult>, RtError>.Err(error);
            }
        }
    }

    private async Task<Result<MutationExecutionResult<RenameOperationResult>, RtError>> ExecuteSingleAsync(
        Microsoft.CodeAnalysis.Solution solution,
        RenameOperationRequest request,
        CancellationToken cancellationToken)
    {
        var prepared = await _rename.PrepareAsync(
            solution,
            new RenameRequest(
                request.Symbol!,
                request.NewName!,
                request.Kind,
                request.DryRun,
                (request.RelatedEdits ?? []).ToHashSet(StringComparer.Ordinal)),
            cancellationToken);
        var renameResult = prepared.Result with { Applied = !request.DryRun };
        var value = new RenameOperationResult(
            RenameOperationMode.Single,
            [renameResult],
            0);
        var changeSet = new PreparedChangeSet(
            solution,
            prepared.Solution,
            movedFiles: prepared.FileMoves);
        var execution = request.DryRun
            ? _executor.Preview(value, changeSet, prepared.Diagnostics)
            : await _executor.ApplyAsync(
                value,
                changeSet,
                prepared.Diagnostics,
                cancellationToken);
        return Result<MutationExecutionResult<RenameOperationResult>, RtError>.Ok(execution);
    }

    private async Task<Result<MutationExecutionResult<RenameOperationResult>, RtError>> ExecuteBatchAsync(
        Microsoft.CodeAnalysis.Solution solution,
        RenameOperationRequest request,
        CancellationToken cancellationToken)
    {
        var basePath = request.WorkingDirectory ?? Directory.GetCurrentDirectory();
        var csvPath = Path.GetFullPath(request.CsvPath!, basePath);
        var prepared = await _rename.PrepareBatchAsync(solution, csvPath, cancellationToken);
        var value = new RenameOperationResult(
            RenameOperationMode.Batch,
            prepared.Results
                .Select(result => result with { Applied = !request.DryRun })
                .ToArray(),
            prepared.Skipped);

        if (prepared.Results.Count == 0)
            return Result<MutationExecutionResult<RenameOperationResult>, RtError>.Ok(
                MutationExecutionResult<RenameOperationResult>.NoOp(
                    value,
                    prepared.Diagnostics));

        var changeSet = new PreparedChangeSet(
            solution,
            prepared.Solution,
            movedFiles: prepared.FileMoves);
        var execution = request.DryRun
            ? _executor.Preview(value, changeSet, prepared.Diagnostics)
            : await _executor.ApplyAsync(
                value,
                changeSet,
                prepared.Diagnostics,
                cancellationToken);
        return Result<MutationExecutionResult<RenameOperationResult>, RtError>.Ok(execution);
    }

    private static RtError? Validate(RenameOperationRequest request)
    {
        return request.Mode switch
        {
            RenameOperationMode.Single when string.IsNullOrWhiteSpace(request.Symbol)
                                            || string.IsNullOrWhiteSpace(request.NewName) =>
                new RtError(
                    RtErrorKind.InvalidInput,
                    "Single rename mode requires symbol and new-name."),
            RenameOperationMode.Single when !string.IsNullOrWhiteSpace(request.CsvPath) =>
                new RtError(
                    RtErrorKind.InvalidInput,
                    "Single rename mode cannot include csv."),
            RenameOperationMode.Batch when string.IsNullOrWhiteSpace(request.CsvPath) =>
                new RtError(
                    RtErrorKind.InvalidInput,
                    "Batch rename mode requires csv."),
            RenameOperationMode.Batch when !string.IsNullOrWhiteSpace(request.Symbol)
                                           || !string.IsNullOrWhiteSpace(request.NewName) =>
                new RtError(
                    RtErrorKind.InvalidInput,
                    "Batch rename mode cannot include symbol or new-name."),
            _ => null
        };
    }
}

internal static class RenameCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "rename",
        true,
        false,
        "refactor",
        Aliases: ["ren"],
        WhenToUse:
        "Renaming a C# symbol (type, method, property, field, namespace) and updating every reference across the solution. Prefer this over Edit or a manual grep+sed: rt uses Roslyn semantic rename so references that only bind by type inference are also updated, and string literals/comments are not accidentally rewritten. Use --csv to batch-apply a curated rename mapping file.",
        Examples:
        [
            "rt refactor rename OldTypeName NewTypeName --solution MySolution.sln",
            "rt refactor rename MyNs.MyType.OldProp NewProp --kind property --dry-run",
            "rt refactor rename --csv scripts/renames.csv --solution MySolution.sln --dry-run",
            "rt refactor ren FooService BarService              # short alias, auto-detect solution",
            "rt inspect find-refs --symbol Foo --json | rt refactor rename --new-name Bar --dry-run   # symbol piped from stdin"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(RenameOperationRequest),
        typeof(MutationExecutionResult<RenameOperationResult>),
        true,
        CommandSafety.Mutation,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
