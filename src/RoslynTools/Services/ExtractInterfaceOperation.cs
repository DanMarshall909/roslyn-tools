using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record ExtractInterfaceRequest(
    [property: JsonRequired]
    [property: CommandPropertyDescription("Class to extract an interface from")]
    string Type,
    [property: CommandPropertyDescription("Directory for the new interface file")]
    string? OutputDir = null,
    [property: CommandPropertyDescription("Namespace override for the interface")]
    string? Namespace = null,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = true);

internal sealed record ExtractInterfaceResult(
    string Type,
    string Interface,
    string Namespace,
    string InterfaceFilePath,
    int MemberCount);

internal sealed class ExtractInterfaceOperation
    : CommandApplicationPort<ExtractInterfaceRequest,
        MutationExecutionResult<ExtractInterfaceResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly ExtractInterfaceService _service;

    public ExtractInterfaceOperation(
        IWorkspaceSessionFactory sessions,
        ExtractInterfaceService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => ExtractInterfaceCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<ExtractInterfaceResult>, RtError>> ExecuteAsync(
        ExtractInterfaceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Type))
            return Result<MutationExecutionResult<ExtractInterfaceResult>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "A class type is required.",
                new Dictionary<string, string> { ["type"] = request.Type }));

        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            var preparationResult = await _service.PrepareAsync(
                solution,
                request.Type,
                request.OutputDir,
                request.Namespace,
                cancellationToken);
            ExtractInterfaceService.Preparation? preparation = null;
            RtError? error = null;
            preparationResult.Match(value => preparation = value, failure => error = failure);
            if (error is not null)
                return Result<MutationExecutionResult<ExtractInterfaceResult>, RtError>.Err(error);

            cancellationToken.ThrowIfCancellationRequested();
            var value = new ExtractInterfaceResult(
                preparation!.TypeName,
                preparation.InterfaceName,
                preparation.Namespace,
                preparation.InterfaceFilePath,
                preparation.MemberCount);
            var changeSet = new PreparedChangeSet(solution, preparation.Solution);
            var execution = request.DryRun
                ? _executor.Preview(value, changeSet)
                : await _executor.ApplyAsync(
                    value,
                    changeSet,
                    cancellationToken: cancellationToken);
            return Result<MutationExecutionResult<ExtractInterfaceResult>, RtError>.Ok(execution);
        }
    }
}

internal static class ExtractInterfaceCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "extract-interface",
        true,
        false,
        "refactor",
        Aliases: ["ei"],
        WhenToUse:
        "Use this instead of manual Edit when generating an IXxx interface from an existing class's public members and updating the class declaration to implement it. Use this before injecting a service as a dependency, or when introducing a seam for testing.",
        Examples:
        [
            "rt refactor extract-interface --type BookingService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor ei --type EmailSender --output-dir src/Interfaces --dry-run",
            "rt refactor extract-interface --type HeroApiClient --namespace ProjectX.Core.Abstractions --solution RoslynTools.slnx"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(ExtractInterfaceRequest),
        typeof(MutationExecutionResult<ExtractInterfaceResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        []);
}
