using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

internal sealed record ArchitectureCheckRequest(
    string? Solution = null,
    [property: CommandPropertyDescription("Path to the checked-in architecture JSON file, relative to the solution directory")]
    string Config = ".rt/architecture.json",
    [property: CommandPropertyDescription("Optional baseline JSON path overriding baselineFile in the configuration")]
    string? Baseline = null);

internal sealed record ArchitectureViolation(
    string Fingerprint,
    string Rule,
    string Layer,
    string Project,
    string File,
    int Line,
    string Dependency,
    string Source,
    [property: JsonPropertyName("baselineState")] string BaselineState);

internal sealed record ArchitectureCheckResult(
    string ConfigPath,
    string? BaselinePath,
    int CheckedProjectCount,
    bool IsCompliant,
    int ViolationCount,
    int NewViolationCount,
    int BaselinedViolationCount,
    IReadOnlyList<ArchitectureViolation> Violations);

internal sealed class ArchitectureCheckOperation(IWorkspaceSessionFactory sessions)
    : CommandApplicationPort<ArchitectureCheckRequest, ArchitectureCheckResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessions = sessions;

    public override ApplicationCommandDefinition Definition => ArchitectureCheckCommandDefinition.Definition;

    public override async Task<Result<ArchitectureCheckResult, RtError>> ExecuteAsync(
        ArchitectureCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Config))
            return Result<ArchitectureCheckResult, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "Architecture configuration path cannot be empty."));

        try
        {
            var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
            using (workspace)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await ArchitectureCheckService.CheckAsync(solution, request, cancellationToken);
            }
        }
        catch (FileNotFoundException ex)
        {
            return Result<ArchitectureCheckResult, RtError>.Err(RtError.NotFound(ex.Message));
        }
        catch (Exception ex) when (ex is IOException
                                   or JsonException
                                   or InvalidOperationException
                                   or ArgumentException)
        {
            return Result<ArchitectureCheckResult, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                ex.Message));
        }
    }
}

internal static class ArchitectureCheckCommandDefinition
{
    internal const string Name = "check_architecture";

    internal static RtCommandInfo Info { get; } = new(
        "check-architecture",
        false,
        true,
        "analyze",
        Aliases: ["arch-check"],
        WhenToUse:
        "Use this when enforcing checked-in clean-architecture boundaries in CI or before moving a feature between projects. It reports project, semantic, package, framework, SDK, and technology leaks with source evidence.",
        Examples:
        [
            "rt analyze check-architecture --solution WageClaims.sln --config .rt/architecture.json --json --trust-repository",
            "rt check-architecture --solution App.slnx --config architecture.json --baseline .rt/architecture-baseline.json --json --trust-repository"
        ],
        KnownIssues:
        [
            "Package, framework, and SDK placement checks inspect declarations in each project file; dependencies introduced only by imported MSBuild files require an explicit semantic or technology rule."
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Yaml, OutputFormat.Llm]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(ArchitectureCheckRequest),
        typeof(ArchitectureCheckResult),
        true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        [],
        product: CommandProductMetadata.FromLegacy(
            Info,
            CommandSafety.ReadOnly,
            effects: CommandEffects.RepositoryRead | CommandEffects.RepositoryCodeExecution,
            workspaceRequirement: WorkspaceRequirement.CompleteSemantic));
}
