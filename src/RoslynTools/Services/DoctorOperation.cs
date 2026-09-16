using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

internal sealed record DoctorRequest(
    [property: JsonRequired]
    [property: CommandPropertyDescription("Absolute repository or workspace directory")]
    string Directory,
    [property: CommandPropertyDescription("Explicit .sln or .slnx path; relative paths resolve under directory")]
    string? Solution = null,
    [property: CommandPropertyDescription("Validate the installed CLI, MCP host, build host, SDK, and protocol without loading a solution")]
    bool InstallationOnly = false);

internal sealed record DoctorEnvironmentProbeResult(
    bool CliLauncherHealthy,
    bool McpLauncherHealthy,
    bool BuildHostHealthy,
    bool CompatibleSdkAvailable,
    bool McpHandshakeHealthy,
    string? ProtocolVersion,
    string? ServerName,
    int DiscoveredTools,
    string? RequestedSdk,
    string? SelectedSdk,
    IReadOnlyList<string> Diagnostics,
    string SdkRequestState = "Absent",
    string? GlobalJsonPath = null,
    string? SdkDiagnostic = null)
{
    internal static DoctorEnvironmentProbeResult Healthy { get; } = new(
        true,
        true,
        true,
        true,
        true,
        "2024-11-05",
        "roslyn-tools-mcp",
        1,
        null,
        "10.0.201",
        [],
        SdkRequestState: "Absent");

    internal bool IsHealthy => CliLauncherHealthy
                               && McpLauncherHealthy
                               && BuildHostHealthy
                               && CompatibleSdkAvailable
                               && McpHandshakeHealthy
                               && DiscoveredTools > 0;
}

internal interface IDoctorEnvironmentProbe
{
    Task<DoctorEnvironmentProbeResult> ProbeAsync(string directory, CancellationToken cancellationToken);
}

internal interface IDoctorWorkspaceBootstrap
{
    Result<bool, RtError> Register(string solutionPath);
}

internal sealed record DoctorInstallation(
    string Version,
    string? ProcessPath,
    bool CliLauncherHealthy,
    bool McpLauncherHealthy,
    bool BuildHostHealthy,
    bool CompatibleSdkAvailable,
    bool McpHandshakeHealthy,
    string? ProtocolVersion,
    string? ServerName,
    int DiscoveredTools,
    string? RequestedSdk,
    string? SelectedSdk,
    string SdkRequestState,
    string? GlobalJsonPath,
    string? SdkDiagnostic,
    bool DaemonRequired);

internal sealed record DoctorRuntime(
    string Framework,
    string OperatingSystem,
    string Architecture);

internal sealed record DoctorWorkspace(
    string State,
    string? SelectedSolution,
    IReadOnlyList<string> Candidates,
    int LoadedProjects,
    string Trust);

internal sealed record DoctorCapabilities(
    int StableCandidates,
    int Preview,
    int Experimental,
    int Public,
    int McpVisible);

internal sealed record DoctorResult(
    DoctorInstallation Installation,
    DoctorRuntime Runtime,
    DoctorWorkspace Workspace,
    DoctorCapabilities Capabilities,
    CommandResourceLimits Limits);

internal sealed class DoctorOperation(
    IReadOnlyWorkspaceSessions sessions,
    IProductCapabilityCatalog capabilities,
    IDoctorEnvironmentProbe environmentProbe,
    IDoctorWorkspaceBootstrap workspaceBootstrap)
    : CommandApplicationPort<DoctorRequest, RtResultEnvelope<DoctorResult>>, IRtService
{
    private readonly IReadOnlyWorkspaceSessions _sessions = sessions;
    private readonly IProductCapabilityCatalog _capabilities = capabilities;
    private readonly IDoctorEnvironmentProbe _environmentProbe = environmentProbe;
    private readonly IDoctorWorkspaceBootstrap _workspaceBootstrap = workspaceBootstrap;

    public override ApplicationCommandDefinition Definition => DoctorCommandDefinition.Definition;

    public override async Task<Result<RtResultEnvelope<DoctorResult>, RtError>> ExecuteAsync(
        DoctorRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Directory) || !Path.IsPathFullyQualified(request.Directory))
            return Result<RtResultEnvelope<DoctorResult>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "Doctor requires an absolute directory so request context is explicit."));

        var stopwatch = Stopwatch.StartNew();
        var root = Path.GetFullPath(request.Directory);
        var candidates = request.InstallationOnly
            ? []
            : DiscoverCandidates(root, request.Solution);
        var probeDirectory = request.InstallationOnly || candidates.Length != 1
            ? root
            : Path.GetDirectoryName(candidates[0]) ?? root;
        var environment = await _environmentProbe.ProbeAsync(probeDirectory, cancellationToken);
        if (request.InstallationOnly)
            return Result<RtResultEnvelope<DoctorResult>, RtError>.Ok(CreateEnvelope(
                root,
                selectedSolution: null,
                [],
                environment.IsHealthy ? "Succeeded" : "Failed",
                "NotRequested",
                loadedProjects: 0,
                "NotRequired",
                environment.Diagnostics,
                stopwatch.ElapsedMilliseconds,
                environment));

        if (candidates.Length != 1)
            return Result<RtResultEnvelope<DoctorResult>, RtError>.Ok(CreateEnvelope(
                root,
                selectedSolution: null,
                candidates,
                candidates.Length == 0 ? "Unsupported" : "Ambiguous",
                "NotLoaded",
                loadedProjects: 0,
                GetTrustOrRequired(root),
                MergeDiagnostics(
                    environment,
                    candidates.Length == 0
                        ? "No .sln or .slnx candidate was found in the requested directory."
                        : "Multiple solution candidates are available; select one explicitly."),
                stopwatch.ElapsedMilliseconds,
                environment));

        var selected = candidates[0];
        if (!RepositoryTrustContext.IsExplicitlyTrustedFor(selected))
            return Result<RtResultEnvelope<DoctorResult>, RtError>.Ok(CreateEnvelope(
                root,
                selected,
                candidates,
                "TrustRequired",
                "NotLoaded",
                loadedProjects: 0,
                "TrustRequired",
                MergeDiagnostics(
                    environment,
                    "Repository evaluation was not performed. After review, use --trust-repository on the CLI invocation or MCP host startup."),
                stopwatch.ElapsedMilliseconds,
                environment));

        try
        {
            var bootstrap = _workspaceBootstrap.Register(selected);
            if (bootstrap.IsErr)
            {
                var error = bootstrap.Match(
                    _ => throw new InvalidOperationException("Expected workspace bootstrap failure."),
                    value => value);
                return Result<RtResultEnvelope<DoctorResult>, RtError>.Ok(CreateEnvelope(
                    root,
                    selected,
                    candidates,
                    "Failed",
                    "Failed",
                    loadedProjects: 0,
                    GetTrust(selected),
                    MergeDiagnostics(environment, error.Message),
                    stopwatch.ElapsedMilliseconds,
                    environment));
            }

            using (var lease = await _sessions.AcquireAsync(selected, cancellationToken))
            {
                var solution = lease.Solution;
                var completeness = await WorkspaceCompleteness.AssessSemanticAsync(
                    solution,
                    cancellationToken);
                var diagnostics = environment.Diagnostics
                    .Concat(completeness.Diagnostics)
                    .ToArray();
                stopwatch.Stop();
                return Result<RtResultEnvelope<DoctorResult>, RtError>.Ok(CreateEnvelope(
                    root,
                    selected,
                    candidates,
                    completeness.State == WorkspaceCompletenessState.Complete && environment.IsHealthy
                        ? "Succeeded"
                        : "Partial",
                    completeness.State.ToString(),
                    solution.ProjectIds.Count,
                    GetTrust(selected),
                    diagnostics,
                    stopwatch.ElapsedMilliseconds,
                    environment,
                    lease.SourceRevision));
            }
        }
        catch (Exception exception) when (exception is IOException
                                          or InvalidOperationException
                                          or UnauthorizedAccessException)
        {
            stopwatch.Stop();
            return Result<RtResultEnvelope<DoctorResult>, RtError>.Ok(CreateEnvelope(
                root,
                selected,
                candidates,
                "Failed",
                "Failed",
                loadedProjects: 0,
                GetTrust(selected),
                MergeDiagnostics(environment, exception.Message),
                stopwatch.ElapsedMilliseconds,
                environment));
        }
    }

    private static string[] MergeDiagnostics(
        DoctorEnvironmentProbeResult environment,
        params string[] diagnostics) => environment.Diagnostics
        .Concat(diagnostics)
        .ToArray();

    private static string[] DiscoverCandidates(string root, string? solution)
    {
        if (!string.IsNullOrWhiteSpace(solution))
        {
            var candidate = Path.IsPathFullyQualified(solution)
                ? Path.GetFullPath(solution)
                : Path.GetFullPath(Path.Combine(root, solution));
            return File.Exists(candidate) ? [candidate] : [];
        }

        if (!System.IO.Directory.Exists(root))
            return [];

        return System.IO.Directory.EnumerateFiles(root, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(System.IO.Directory.EnumerateFiles(root, "*.slnx", SearchOption.TopDirectoryOnly))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private RtResultEnvelope<DoctorResult> CreateEnvelope(
        string root,
        string? selectedSolution,
        string[] candidates,
        string outcome,
        string workspaceState,
        int loadedProjects,
        string trust,
        IReadOnlyList<string> diagnostics,
        long elapsedMilliseconds,
        DoctorEnvironmentProbeResult environment,
        string? sourceRevision = null)
    {
        var limits = Definition.Product.Limits;
        var definitions = _capabilities.GetDefinitions();
        var result = new DoctorResult(
            new DoctorInstallation(
                Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                Environment.ProcessPath,
                environment.CliLauncherHealthy,
                environment.McpLauncherHealthy,
                environment.BuildHostHealthy,
                environment.CompatibleSdkAvailable,
                environment.McpHandshakeHealthy,
                environment.ProtocolVersion,
                environment.ServerName,
                environment.DiscoveredTools,
                environment.RequestedSdk,
                environment.SelectedSdk,
                environment.SdkRequestState,
                environment.GlobalJsonPath,
                environment.SdkDiagnostic,
                DaemonRequired: false),
            new DoctorRuntime(
                System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()),
            new DoctorWorkspace(workspaceState, selectedSolution, candidates, loadedProjects, trust),
            new DoctorCapabilities(
                definitions.Count(definition => definition.Product.Maturity == CapabilityMaturity.StableCandidate),
                definitions.Count(definition => definition.Product.Maturity == CapabilityMaturity.Preview),
                definitions.Count(definition => definition.Product.Maturity == CapabilityMaturity.Experimental),
                definitions.Count(definition => definition.Product.Audience == CommandAudience.Public),
                definitions.Count(definition => definition.McpVisible)),
            limits);
        var limitations = trust switch
        {
            "OneShotTrusted" => ["Trust applies only to this invocation."],
            "PersistentTrusted" => ["Persistent trust uses preview registered-worktree and Git-object continuity checks."],
            "NotRequired" => Array.Empty<string>(),
            _ => ["Repository build inputs were not evaluated without explicit trust."]
        };

        return new RtResultEnvelope<DoctorResult>(
            "rt.result.v1",
            "doctor",
            outcome,
            new RtSourceIdentity(root, selectedSolution, sourceRevision),
            new RtWorkspaceProvenance(workspaceState, loadedProjects, diagnostics),
            result,
            new RtConfidence(
                outcome == "Succeeded" ? "High" : "Limited",
                "Local launcher, runtime, catalogue, and workspace checks",
                [$"Discovered {candidates.Length} solution candidate(s)."],
                limitations),
            diagnostics.Select((message, index) =>
                new RtStructuredDiagnostic($"doctor.{index + 1}", "Warning", message)).ToArray(),
            limitations,
            outcome == "TrustRequired"
                ? [new RtNextAction("doctor", "Review the repository, then opt into one trusted evaluation.")]
                : [],
            new RtResultSafety(["LocalStateRead", "RepositoryRead"], false, false),
            new RtTiming(elapsedMilliseconds),
            new RtPaging(candidates.Length, candidates.Length, false, ContinuationToken: null));
    }

    private static string GetTrust(string path) =>
        RepositoryTrustContext.IsPersistentlyTrustedFor(path) ? "PersistentTrusted" : "OneShotTrusted";

    private static string GetTrustOrRequired(string path) =>
        RepositoryTrustContext.IsPersistentlyTrustedFor(path)
            ? "PersistentTrusted"
            : RepositoryTrustContext.IsExplicitlyTrustedFor(path)
                ? "OneShotTrusted"
                : "TrustRequired";
}

internal static class DoctorCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "doctor",
        false,
        true,
        "inspect",
        "Use this when starting work in a repository to verify launcher, runtime, solution selection, trust, workspace readiness, capability maturity, and resource limits without silently evaluating unreviewed build inputs.",
        [
            "rt inspect doctor --directory C:\\code\\app",
            "rt inspect doctor --directory C:\\code\\app --solution App.slnx --trust-repository --json"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(DoctorRequest),
        typeof(RtResultEnvelope<DoctorResult>),
        mcpVisible: true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        [],
        product: CommandProductMetadata.FromLegacy(
            Info,
            CommandSafety.ReadOnly,
            maturity: CapabilityMaturity.StableCandidate));
}
