using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for creating and registering a typed IOptions POCO.</summary>
internal sealed record MigrateToIOptionsRequest(
    [property: JsonRequired,
     CommandPropertyDescription("Configuration section name, for example MyFeature or Parent:Child")]
    string Section,
    [property: JsonRequired,
     CommandPropertyDescription("Options POCO type name in PascalCase")]
    string Name,
    [property: CommandPropertyDescription("Path to the .sln/.slnx file")]
    string? Solution = null,
    [property: JsonPropertyName("dry-run"),
     CommandPropertyDescription("Preview generated POCO and registration changes without writing")]
    bool DryRun = true);

/// <summary>One DI site considered when preparing the migration.</summary>
internal sealed record MigrateToIOptionsRegistrationSite(
    string FilePath,
    int Line,
    string ServicesExpression,
    string ConfigurationExpression);

/// <summary>Structured evidence produced by an IOptions migration preparation.</summary>
internal sealed record MigrateToIOptionsResult(
    string Section,
    string GeneratedType,
    string GeneratedPath,
    MigrateToIOptionsRegistrationSite RegistrationSite,
    IReadOnlyList<string> DiscoveredConfigSources,
    IReadOnlyList<string> ReadSiteWorklist,
    bool AlreadyApplied);

/// <summary>Typed application port for the IOptions migration.</summary>
internal sealed class MigrateToIOptionsOperation(
    IWorkspaceSessionFactory sessions,
    MigrateToIOptionsService service,
    ISolutionChangeWriter writer)
    : CommandApplicationPort<MigrateToIOptionsRequest,
        MutationExecutionResult<MigrateToIOptionsResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor = new(
        new SolutionPreparedChangeSetWriter(writer));
    private readonly IWorkspaceSessionFactory _sessions = sessions;
    private readonly MigrateToIOptionsService _service = service;

    public override ApplicationCommandDefinition Definition => MigrateToIOptionsCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<MigrateToIOptionsResult>, RtError>> ExecuteAsync(
        MigrateToIOptionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = Validate(request);
        if (validationError is not null)
            return Result<MutationExecutionResult<MigrateToIOptionsResult>, RtError>.Err(validationError);

        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var preparationResult = await _service.PrepareAsync(
                solution,
                request.Section,
                request.Name,
                cancellationToken);
            if (preparationResult.IsErr)
                return preparationResult.Match(
                    _ => throw new InvalidOperationException("An invalid IOptions migration preparation was accepted."),
                    error => Result<MutationExecutionResult<MigrateToIOptionsResult>, RtError>.Err(error));

            var preparation = preparationResult.Unwrap();
            if (!preparation.Changed)
                return Result<MutationExecutionResult<MigrateToIOptionsResult>, RtError>.Ok(
                    MutationExecutionResult<MigrateToIOptionsResult>.NoOp(
                        preparation.Result,
                        preparation.Diagnostics));

            cancellationToken.ThrowIfCancellationRequested();
            var changeSet = new PreparedChangeSet(solution, preparation.Solution);
            var execution = request.DryRun
                ? _executor.Preview(preparation.Result, changeSet, preparation.Diagnostics)
                : await _executor.ApplyAsync(
                    preparation.Result,
                    changeSet,
                    preparation.Diagnostics,
                    cancellationToken);
            return Result<MutationExecutionResult<MigrateToIOptionsResult>, RtError>.Ok(execution);
        }
    }

    private static RtError? Validate(MigrateToIOptionsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Section))
            return new RtError(RtErrorKind.InvalidInput, "--section is required.");

        if (request.Section.StartsWith(':')
            || request.Section.EndsWith(':')
            || request.Section.Contains("::", StringComparison.Ordinal))
            return new RtError(
                RtErrorKind.InvalidInput,
                "--section is malformed. Use non-empty ':'-separated names without empty segments.");

        if (string.IsNullOrWhiteSpace(request.Name))
            return new RtError(RtErrorKind.InvalidInput, "--name is required.");

        if (!char.IsUpper(request.Name[0])
            || request.Name.Any(character => !char.IsLetterOrDigit(character) && character != '_'))
            return new RtError(RtErrorKind.InvalidInput, "--name must be PascalCase.");

        return null;
    }
}

internal static class MigrateToIOptionsCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "migrate-to-ioptions",
        true,
        false,
        "refactor",
        Aliases: ["mti"],
        WhenToUse:
        "Use this instead of manual Edit when introducing a typed IOptions POCO for an existing configuration section; it creates the POCO and registration while leaving read-site rewrites as an explicit follow-up worklist.",
        Examples:
        [
            "rt refactor migrate-to-ioptions --section MyFeature --name MyFeatureOptions --solution RoslynTools.slnx",
            "rt refactor migrate-to-ioptions --section MyFeature --name MyFeatureOptions --dry-run"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(MigrateToIOptionsRequest),
        typeof(MutationExecutionResult<MigrateToIOptionsResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        []);
}
