using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record SafeDeleteRequest(
    [property: JsonRequired]
    [property: CommandPropertyDescription("Type or member FQN or simple name to delete")]
    string Symbol,
    [property: CommandPropertyDescription("Kind of symbol: type, method, or property")]
    string? Kind = null,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = false);

internal sealed record SafeDeleteReferenceEvidence(
    string FilePath,
    int Line,
    string LineText);

internal sealed record SafeDeleteResult(
    string Symbol,
    string Kind,
    string DeclarationPath,
    int ReferenceCount,
    int IgnoredNameofReferenceCount);

internal sealed class SafeDeleteOperation
    : CommandApplicationPort<SafeDeleteRequest, MutationExecutionResult<SafeDeleteResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly SafeDeleteService _service;

    public SafeDeleteOperation(
        IWorkspaceSessionFactory sessions,
        SafeDeleteService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => SafeDeleteCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<SafeDeleteResult>, RtError>> ExecuteAsync(
        SafeDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol))
            return Result<MutationExecutionResult<SafeDeleteResult>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "A symbol to delete is required."));

        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            var preparationResult = await _service.PrepareAsync(
                solution,
                request.Symbol,
                request.Kind,
                cancellationToken);
            SafeDeleteService.Preparation? preparation = null;
            RtError? error = null;
            preparationResult.Match(value => preparation = value, failure => error = failure);
            if (error is not null)
                return Result<MutationExecutionResult<SafeDeleteResult>, RtError>.Err(error);

            if (preparation!.References.Count != 0)
                return Result<MutationExecutionResult<SafeDeleteResult>, RtError>.Err(
                    CreateReferencedSymbolError(preparation));

            cancellationToken.ThrowIfCancellationRequested();
            var value = new SafeDeleteResult(
                preparation.Symbol,
                preparation.Kind,
                preparation.DeclarationPath,
                preparation.References.Count,
                preparation.IgnoredNameofReferenceCount);
            var changeSet = new PreparedChangeSet(solution, preparation.Solution);
            var execution = request.DryRun
                ? _executor.Preview(value, changeSet)
                : await _executor.ApplyAsync(
                    value,
                    changeSet,
                    cancellationToken: cancellationToken);
            return Result<MutationExecutionResult<SafeDeleteResult>, RtError>.Ok(execution);
        }
    }

    private static RtError CreateReferencedSymbolError(SafeDeleteService.Preparation preparation)
    {
        var context = new Dictionary<string, string>
        {
            ["symbol"] = preparation.Symbol,
            ["referenceCount"] = preparation.References.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        for (var index = 0; index < preparation.References.Count; index++)
        {
            var reference = preparation.References[index];
            context[$"reference.{index}.filePath"] = reference.FilePath;
            context[$"reference.{index}.line"] = reference.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
            context[$"reference.{index}.lineText"] = reference.LineText;
        }

        var evidence = string.Join(
            "; ",
            preparation.References.Select(reference =>
                $"{reference.FilePath}({reference.Line}): {reference.LineText}"));
        return new RtError(
            RtErrorKind.InvalidInput,
            $"Cannot delete '{preparation.Symbol}': it has {preparation.References.Count} reference(s). References: {evidence}",
            context);
    }
}

internal static class SafeDeleteCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "safe-delete",
        true,
        false,
        "refactor",
        Aliases: ["sd"],
        WhenToUse:
        "Use this instead of a manual Edit when deleting a type or member only after verifying it has zero references in the solution. The command checks references (excluding nameof() and the declaration itself) before touching any file, and exits non-zero with reference evidence if usages exist.",
        Examples:
        [
            "rt refactor safe-delete --symbol OrphanedHelper --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor sd --symbol BookingService.LegacyMethod --kind method --dry-run --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor safe-delete --symbol OldDtoType --kind type --solution RoslynTools.slnx"
        ],
        SeeAlso: ["find-unused", "find-refs"]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(SafeDeleteRequest),
        typeof(MutationExecutionResult<SafeDeleteResult>),
        true,
        CommandSafety.Mutation,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
