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
    string? Solution = null);

internal sealed record DoctorInstallation(
    string Version,
    string? ProcessPath,
    bool CliLauncherHealthy,
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
    IWorkspaceSessionFactory sessions,
    IProductCapabilityCatalog capabilities)
    : CommandApplicationPort<DoctorRequest, RtResultEnvelope<DoctorResult>>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessions = sessions;
    private readonly IProductCapabilityCatalog _capabilities = capabilities;

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
        var candidates = DiscoverCandidates(root, request.Solution);
        if (candidates.Length != 1)
            return Result<RtResultEnvelope<DoctorResult>, RtError>.Ok(CreateEnvelope(
                root,
                selectedSolution: null,
                candidates,
                candidates.Length == 0 ? "Unsupported" : "Ambiguous",
                "NotLoaded",
                loadedProjects: 0,
                RepositoryTrustContext.IsExplicitlyTrusted ? "OneShotTrusted" : "TrustRequired",
                candidates.Length == 0
                    ? ["No .sln or .slnx candidate was found in the requested directory."]
                    : ["Multiple solution candidates are available; select one explicitly."],
                stopwatch.ElapsedMilliseconds));

        var selected = candidates[0];
        if (!RepositoryTrustContext.IsExplicitlyTrusted)
            return Result<RtResultEnvelope<DoctorResult>, RtError>.Ok(CreateEnvelope(
                root,
                selected,
                candidates,
                "TrustRequired",
                "NotLoaded",
                loadedProjects: 0,
                "TrustRequired",
                ["Repository evaluation was not performed. Re-run with --trust-repository after review."],
                stopwatch.ElapsedMilliseconds));

        try
        {
            var (workspace, solution) = await _sessions.LoadWorkspaceAsync(selected);
            using (workspace)
            {
                var completeness = await WorkspaceCompleteness.AssessSemanticAsync(
                    solution,
                    cancellationToken);
                stopwatch.Stop();
                return Result<RtResultEnvelope<DoctorResult>, RtError>.Ok(CreateEnvelope(
                    root,
                    selected,
                    candidates,
                    completeness.State == WorkspaceCompletenessState.Complete
                        ? "Succeeded"
                        : "Partial",
                    completeness.State.ToString(),
                    solution.ProjectIds.Count,
                    "OneShotTrusted",
                    completeness.Diagnostics,
                    stopwatch.ElapsedMilliseconds));
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
                "OneShotTrusted",
                [exception.Message],
                stopwatch.ElapsedMilliseconds));
        }
    }

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
        long elapsedMilliseconds)
    {
        var limits = Definition.Product.Limits;
        var definitions = _capabilities.GetDefinitions();
        var result = new DoctorResult(
            new DoctorInstallation(
                Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                Environment.ProcessPath,
                !string.IsNullOrWhiteSpace(Environment.ProcessPath),
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
        var limitations = trust == "OneShotTrusted"
            ? new[] { "Trust is invocation-scoped; a protected persistent trust registry is not implemented yet." }
            : new[] { "Repository build inputs were not evaluated without explicit trust." };

        return new RtResultEnvelope<DoctorResult>(
            "rt.result.v1",
            "doctor",
            outcome,
            new RtSourceIdentity(root, selectedSolution, Revision: null),
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
        CommandExecutionPlacement.InProcess,
        [],
        product: CommandProductMetadata.FromLegacy(
            Info,
            CommandSafety.ReadOnly,
            maturity: CapabilityMaturity.StableCandidate));
}
