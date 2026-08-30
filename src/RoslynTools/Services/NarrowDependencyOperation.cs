using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record NarrowDependencyRequest(
    [property: JsonRequired]
    [property: JsonPropertyName("interface")]
    [property: CommandPropertyDescription("Source interface whose member subset will be extracted")]
    string SourceInterface,
    [property: JsonRequired]
    [property: CommandPropertyDescription("Member names to extract; use an array for multiple members")]
    string[] Members,
    [property: JsonRequired]
    [property: JsonPropertyName("new-name")]
    [property: CommandPropertyDescription("Name of the new narrower interface")]
    string NewInterfaceName,
    [property: CommandPropertyDescription("Path to .sln/.slnx")]
    string? Solution = null,
    [property: JsonPropertyName("dry-run")]
    [property: CommandPropertyDescription("Preview without writing")]
    bool DryRun = false);

internal sealed record NarrowDependencySkippedConsumer(string Name, string Reason);

internal sealed record NarrowDependencyResult(
    string SourceInterface,
    string NewInterfaceName,
    bool InterfaceCreated,
    bool InterfaceReused,
    IReadOnlyList<string> NarrowedConsumers,
    IReadOnlyList<NarrowDependencySkippedConsumer> SkippedConsumers,
    string? DiRegistrationFollowUp);

internal sealed class NarrowDependencyOperation
    : CommandApplicationPort<NarrowDependencyRequest, MutationExecutionResult<NarrowDependencyResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly NarrowDependencyService _service;

    public NarrowDependencyOperation(
        IWorkspaceSessionFactory sessions,
        NarrowDependencyService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => NarrowDependencyCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<NarrowDependencyResult>, RtError>> ExecuteAsync(
        NarrowDependencyRequest request,
        CancellationToken cancellationToken = default)
    {
        var inputError = ValidateRequest(request);
        if (inputError is not null)
            return Result<MutationExecutionResult<NarrowDependencyResult>, RtError>.Err(inputError);

        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            var preparationResult = await _service.PrepareAsync(
                solution,
                request.SourceInterface,
                request.Members,
                request.NewInterfaceName,
                cancellationToken);
            NarrowDependencyService.Preparation? preparation = null;
            RtError? error = null;
            preparationResult.Match(value => preparation = value, failure => error = failure);
            if (error is not null)
                return Result<MutationExecutionResult<NarrowDependencyResult>, RtError>.Err(error);

            cancellationToken.ThrowIfCancellationRequested();
            var value = new NarrowDependencyResult(
                preparation!.SourceInterface,
                preparation.NewInterfaceName,
                preparation.InterfaceCreated,
                preparation.InterfaceReused,
                preparation.NarrowedConsumers,
                preparation.SkippedConsumers,
                preparation.DiRegistrationFollowUp);
            var diagnostics = CreateDiagnostics(preparation);
            var changeSet = new PreparedChangeSet(solution, preparation.Solution);
            if (!changeSet.HasChanges)
                return Result<MutationExecutionResult<NarrowDependencyResult>, RtError>.Ok(
                    MutationExecutionResult<NarrowDependencyResult>.NoOp(value, diagnostics));

            var execution = request.DryRun
                ? _executor.Preview(value, changeSet, diagnostics)
                : await _executor.ApplyAsync(value, changeSet, diagnostics, cancellationToken);
            return Result<MutationExecutionResult<NarrowDependencyResult>, RtError>.Ok(execution);
        }
    }

    private static RtError? ValidateRequest(NarrowDependencyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceInterface))
            return new RtError(RtErrorKind.InvalidInput, "A source interface is required.");
        if (request.Members is null || request.Members.Length == 0)
            return new RtError(RtErrorKind.InvalidInput, "At least one interface member is required.");
        if (string.IsNullOrWhiteSpace(request.NewInterfaceName))
            return new RtError(RtErrorKind.InvalidInput, "A new interface name is required.");
        return null;
    }

    private static List<MutationDiagnostic> CreateDiagnostics(
        NarrowDependencyService.Preparation preparation)
    {
        var diagnostics = new List<MutationDiagnostic>
        {
            new(
                MutationDiagnosticSeverity.Information,
                preparation.InterfaceReused
                    ? $"Reusing existing interface '{preparation.NewInterfaceName}'."
                    : $"New interface '{preparation.NewInterfaceName}' will be created.")
        };
        if (preparation.DiRegistrationFollowUp is not null)
            diagnostics.Add(new MutationDiagnostic(
                MutationDiagnosticSeverity.Information,
                preparation.DiRegistrationFollowUp));
        return diagnostics;
    }
}

internal static class NarrowDependencyCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "narrow-dependency",
        true,
        false,
        "refactor",
        Aliases: ["nd"],
        WhenToUse:
        "Use this instead of a manual Edit for interface-segregation refactoring: it extracts a named subset of an interface's members into a new narrower interface and rewrites consumers that only use that subset to depend on the narrower type. Consumers that require members outside the subset are left unchanged and reported. DI registration updates are reported as a manual follow-up.",
        Examples:
        [
            "rt refactor narrow-dependency IWideFacade --members Send --new-name ISender --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor nd IEmailService --members Send,SendBulk --new-name IEmailSender --solution RoslynTools.slnx --dry-run",
            "rt refactor narrow-dependency ProjectX.Core.IRepository --members GetById,GetAll --new-name IReadOnlyRepository --solution C:\\code\\libraries\\ProjectX.Libraries.sln"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(NarrowDependencyRequest),
        typeof(MutationExecutionResult<NarrowDependencyResult>),
        true,
        CommandSafety.Mutation,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
