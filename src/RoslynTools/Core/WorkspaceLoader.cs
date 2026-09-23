using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

using RoslynTools.Core.Output;

namespace RoslynTools.Core;

/// <summary>
/// Loads and manages workspaces for solution processing.
/// </summary>
internal sealed class WorkspaceLoader
    : IWorkspaceSessionFactory, IWorkspaceSnapshotLoader, IWorkspaceLoadConfiguration, IDisposable
{
    private static readonly SemaphoreSlim SolutionLoadGate = new(1, 1);

    // MSBuild property name used to redirect obj/ output; suffixed with a directory separator
    // so MSBuild treats it as a folder (otherwise `obj.123/file.dll` style paths result).
    internal const string BaseIntermediateOutputPathProperty = "BaseIntermediateOutputPath";

    // Env var that opts in to per-process obj isolation. See ResolveIsolatedObjPath.
    internal const string IsolatedObjEnvVar = "RT_ISOLATED_OBJ";

    private MSBuildWorkspace? _workspace;

    // ── Test hooks ──────────────────────────────────────────────────────────

    // AsyncLocal so parallel xunit test methods don't clobber each other.
    /// <summary>
    /// Overrides the default behavior of the workspace loader.
    /// </summary>
    internal static AsyncLocal<(Workspace Workspace, Solution Solution)?> Override { get; } = new();

    // AsyncLocal so parallel xunit tests don't clobber each other.
    /// <summary>
    /// Overrides the diagnostic sink for the workspace loader.
    /// </summary>
    internal static AsyncLocal<TextWriter?> DiagnosticSinkOverride { get; } = new();

    internal static AsyncLocal<Func<MSBuildWorkspace, string, Task<Solution>>?> OpenSolutionOverride { get; } = new();
    internal static AsyncLocal<Func<Task>?> AfterDiscoveryOverride { get; } = new();

    public void Dispose()
    {
        _workspace?.Dispose();
        _workspace = null;
    }

    // Test-only scope: installs an override for the duration of the returned IDisposable.
    /// <summary>
    /// Applies override settings to a workspace and solution.
    /// </summary>
    internal static IDisposable UseOverride(Workspace workspace, Solution solution)
    {
        Override.Value = (workspace, solution);
        return new OverrideScope();
    }

    // ── Instance API (DI path) ───────────────────────────────────────────────

    /// <summary>
    ///     Load and return the solution. The instance retains the workspace — do not dispose it
    ///     from the call site. The workspace is disposed when this <see cref="WorkspaceLoader" />
    ///     is disposed (i.e. when the DI container tears down). Repository evaluation must have
    ///     been explicitly authorized by the trusted host invocation.
    /// </summary>
    /// <exception cref="RepositoryTrustRequiredException">
    ///     The current invocation has not explicitly authorized repository evaluation.
    /// </exception>
    public async Task<Solution> GetSolutionAsync(string? solutionPath)
    {
        if (Override.Value is { } o)
            return o.Solution;

        var (workspace, solution) = await LoadCoreAsync(solutionPath, requireTrust: true);
        _workspace?.Dispose();
        _workspace = workspace;
        return solution;
    }

    /// <summary>
    ///     Compatibility instance API for commands that still need workspace ownership while
    ///     they migrate to solution-only operations. Repository evaluation must have been
    ///     explicitly authorized by the trusted host invocation. The loader retains ownership
    ///     of the workspace and disposes it when replaced or when this loader is disposed.
    /// </summary>
    /// <exception cref="RepositoryTrustRequiredException">
    ///     The current invocation has not explicitly authorized repository evaluation.
    /// </exception>
    public async Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath)
    {
        if (Override.Value is { } o)
            return (o.Workspace, o.Solution);

        var (workspace, solution) = await LoadCoreAsync(solutionPath, requireTrust: true);
        _workspace?.Dispose();
        _workspace = workspace;
        return (workspace, solution);
    }

    /// <summary>
    ///     Loads a trusted, caller-owned workspace and establishes its authoritative initial
    ///     completeness assessment. A solution-open failure is represented as typed failed
    ///     evidence when a workspace exists to carry it; missing files and trust failures throw.
    /// </summary>
    /// <returns>The acquired workspace, its solution snapshot, and typed completeness evidence.</returns>
    /// <exception cref="FileNotFoundException">The selected solution file does not exist.</exception>
    /// <exception cref="RepositoryTrustRequiredException">Repository evaluation was not authorized.</exception>
    /// <exception cref="OperationCanceledException">The operation was cancelled.</exception>
    public async Task<WorkspaceLoadResult> LoadWorkspaceResultAsync(
        string? solutionPath,
        CancellationToken cancellationToken = default)
    {
        if (Override.Value is { } overridden)
        {
            var overriddenCompleteness = await WorkspaceCompleteness.AssessSemanticAsync(
                overridden.Solution,
                cancellationToken);
            return new WorkspaceLoadResult(overridden.Workspace, overridden.Solution, overriddenCompleteness);
        }

        var (workspace, solution) = await LoadCoreAsync(
            solutionPath,
            requireTrust: true,
            returnTypedFailure: true,
            cancellationToken: cancellationToken);
        try
        {
            var completeness = await WorkspaceCompleteness.AssessSemanticAsync(solution, cancellationToken);
            _workspace?.Dispose();
            _workspace = workspace;
            return new WorkspaceLoadResult(workspace, solution, completeness);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    async Task<OwnedWorkspaceSnapshot> IWorkspaceSnapshotLoader.LoadOwnedWorkspaceAsync(
        string? solutionPath,
        CancellationToken cancellationToken)
    {
        if (Override.Value is { } o)
            return new OwnedWorkspaceSnapshot(o.Workspace, o.Solution);

        var (workspace, solution) = await LoadCoreAsync(
            solutionPath,
            requireTrust: true,
            returnTypedFailure: true,
            cancellationToken: cancellationToken);
        return new OwnedWorkspaceSnapshot(workspace, solution);
    }

    // ── Static API (test-only convenience) ─────────────────────────────────

    /// <summary>
    ///     Test-only convenience for smoke tests and architecture checks that need a
    ///     one-liner load without the DI ceremony. Caller owns the returned workspace
    ///     and must dispose it. Production code must use <see cref="IWorkspaceSessionFactory" />
    ///     — the <c>NoStaticWorkspaceLoaderUse</c> architecture gate enforces this.
    /// </summary>
    internal static async Task<(Workspace Workspace, Solution Solution)> LoadAsync(string? solutionPath)
    {
        if (Override.Value is { } o)
            return (o.Workspace, o.Solution);

        var (workspace, solution) = await LoadCoreAsync(solutionPath, requireTrust: false);
        return (workspace, solution);
    }

    // ── Obj isolation (AC4 of US-075) ───────────────────────────────────────

    /// <summary>
    ///     Returns the per-process <c>BaseIntermediateOutputPath</c> when
    ///     <c>RT_ISOLATED_OBJ=1</c> is set in the environment, otherwise
    ///     <see langword="null" /> (shared obj/ — default).
    ///
    ///     Set this env var on parallel-agent shells so simultaneous
    ///     <c>rt analyze …</c> invocations don't race on shared <c>obj/Debug</c>
    ///     file locks. Trade-off: every process gets a cold obj cache.
    /// </summary>
    internal static string? ResolveIsolatedObjPath()
    {
        var flag = Environment.GetEnvironmentVariable(IsolatedObjEnvVar);
        if (string.IsNullOrWhiteSpace(flag) || flag is "0" or "false" or "False")
            return null;

        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        var path = Path.Combine(Path.GetTempPath(), "rt-obj", pid);
        // Trailing separator: MSBuild requires it on directory-shaped properties.
        return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
    }

    string? IWorkspaceLoadConfiguration.BaseIntermediateOutputPath => ResolveIsolatedObjPath();

    // ── Shared load logic ───────────────────────────────────────────────────

    private static async Task<(MSBuildWorkspace Workspace, Solution Solution)> LoadCoreAsync(
        string? solutionPath,
        bool requireTrust,
        bool returnTypedFailure = false,
        CancellationToken cancellationToken = default)
    {
        solutionPath = SolutionPathResolver.Resolve(solutionPath);

        if (!File.Exists(solutionPath))
            throw new FileNotFoundException($"Solution not found: {solutionPath}", solutionPath);

        if (requireTrust && !RepositoryTrustContext.IsExplicitlyTrustedFor(solutionPath))
            throw RepositoryTrustRequiredException.ForSolution(solutionPath);
        var workspaceIdentity = RepositoryTrustContext.IsExplicitlyTrustedFor(solutionPath)
            ? RepositoryTrustContext.ResolveWorkspaceIdentity(solutionPath)
            : null;

        var properties = new Dictionary<string, string>
        {
            // Essential for legacy web projects that import TypeScript/EF Design targets
            // which may not be present in all environments.
            { "SkipUnrecognizedProjects", "true" }
        };

        // AC4 of US-075: when RT_ISOLATED_OBJ=1, redirect obj/ output to a per-process
        // temp directory so parallel `rt analyze ...` agents don't race on shared
        // obj/Debug file locks. Opt-in to preserve cold-start obj cache for humans.
        var isolatedObj = ResolveIsolatedObjPath();
        if (isolatedObj is not null)
            properties[BaseIntermediateOutputPathProperty] = isolatedObj;

        var provenance = new WorkspaceEvaluatorProvenance(
            RepositoryRoot: null,
            solutionPath,
            SourceFingerprint: null,
            Configuration: "MSBuild default (not captured)",
            TargetFrameworks: [],
            SdkIdentity: "Unknown",
            CompilerIdentity: typeof(Compilation).Assembly.GetName().Version?.ToString() ?? "Unknown",
            AnalyzerGeneratorPolicy: "Workspace defaults; stage completion not captured",
            TrustState: RepositoryTrustContext.IsExplicitlyTrustedFor(solutionPath) ? "Trusted" : "NotEstablished",
            EvaluatorMode: "InProcess");
        var seed = await WorkspaceManifestDiscovery.SeedAsync(solutionPath, cancellationToken);
        var workspace = MSBuildWorkspace.Create(properties);
        WorkspaceAcquisitionRegistry.Declare(workspace, seed.Manifest, provenance);
        WorkspaceDiagnosticRegistry.Capture(workspace);
        foreach (var diagnostic in seed.Diagnostics)
            WorkspaceDiagnosticRegistry.Record(workspace, diagnostic);

        var seedProjectPaths = seed.Manifest.Inputs
            .Where(input => input.Kind == SemanticInputKind.Project)
            .Select(input => input.ProjectPath ?? input.Path ?? input.Id)
            .ToArray();
        WorkspaceInputFingerprint? discoveryFingerprint = null;
        WorkspaceInputFingerprintResult? preDiscoveryInputSet = null;
        if (workspaceIdentity is not null)
        {
            try
            {
                discoveryFingerprint = WorkspaceInputFingerprint.CreateForDiscovery();
                preDiscoveryInputSet = await discoveryFingerprint.CaptureRepositoryAsync(
                    workspaceIdentity,
                    seedProjectPaths,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or InvalidOperationException)
            {
                WorkspaceDiagnosticRegistry.Record(workspace, new WorkspaceAcquisitionDiagnostic(
                    "acquisition.discovery-membership",
                    DiagnosticSeverity.Error,
                    CompletenessDiagnosticImpact.RequiredInputIncomplete,
                    "workspace-input-fingerprint",
                    exception.Message));
            }
        }

        WorkspaceAcquisitionDiscovery discovery;
        try
        {
            discovery = await WorkspaceAcquisitionCollector.DiscoverAsync(seed, properties, cancellationToken);
            provenance = provenance with
            {
                Configuration = discovery.Configuration,
                TargetFrameworks = discovery.TargetFrameworks,
                SdkIdentity = discovery.SdkIdentity,
                AnalyzerGeneratorPolicy = "MSBuild inputs evaluated; stage completion pending Roslyn acquisition",
                EvaluatorMode = "MSBuild evaluation plus Roslyn acquisition"
            };
            WorkspaceAcquisitionRegistry.RefineDeclaration(workspace, seed.Manifest, discovery.Manifest, provenance);
            foreach (var diagnostic in discovery.Diagnostics.Except(seed.Diagnostics))
                WorkspaceDiagnosticRegistry.Record(workspace, diagnostic);
            if (AfterDiscoveryOverride.Value is { } afterDiscovery)
                await afterDiscovery();
        }
        catch
        {
            workspace.Dispose();
            throw;
        }

        WorkspaceInputFingerprint? inputFingerprint = null;
        WorkspaceInputFingerprintResult? preLoadInputSet = null;
        var discoveryWindowStable = false;
        if (workspaceIdentity is not null)
        {
            try
            {
                var discoveredProjectPaths = discovery.Manifest.Inputs
                    .Where(input => input.Kind == SemanticInputKind.Project)
                    .Select(input => input.ProjectPath ?? input.Path ?? input.Id)
                    .ToArray();
                inputFingerprint = WorkspaceInputFingerprint.CreateForAcquisition(discovery.SdkIdentity);
                preLoadInputSet = await inputFingerprint.CaptureRepositoryAsync(
                    workspaceIdentity,
                    discoveredProjectPaths,
                    cancellationToken);
                var postDiscoveryInputSet = discoveryFingerprint is null
                    ? (WorkspaceInputFingerprintResult?)null
                    : await discoveryFingerprint.CaptureRepositoryAsync(
                        workspaceIdentity,
                        seedProjectPaths,
                        cancellationToken);
                discoveryWindowStable = preDiscoveryInputSet is { } beforeDiscovery
                    && postDiscoveryInputSet is { } afterDiscovery
                    && string.Equals(beforeDiscovery.Value, afterDiscovery.Value, StringComparison.Ordinal);
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or InvalidOperationException)
            {
                WorkspaceDiagnosticRegistry.Record(workspace, new WorkspaceAcquisitionDiagnostic(
                    "acquisition.input-membership",
                    DiagnosticSeverity.Error,
                    CompletenessDiagnosticImpact.RequiredInputIncomplete,
                    "workspace-input-fingerprint",
                    exception.Message));
            }
        }

        Solution solution;
        try
        {
            await SolutionLoadGate.WaitAsync(cancellationToken);
            try
            {
                solution = OpenSolutionOverride.Value is { } openSolution
                    ? await openSolution(workspace, solutionPath)
                    : await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
            }
            finally
            {
                SolutionLoadGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            EmitDiagnostics(WorkspaceDiagnosticRegistry.Get(workspace), true);
            workspace.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            if (!returnTypedFailure)
            {
                EmitDiagnostics(WorkspaceDiagnosticRegistry.Get(workspace), true);
                workspace.Dispose();
                throw;
            }

            WorkspaceDiagnosticRegistry.Record(workspace, new WorkspaceAcquisitionDiagnostic(
                "acquisition.solution-load",
                DiagnosticSeverity.Error,
                CompletenessDiagnosticImpact.MinimumScopeUnavailable,
                "msbuild-workspace",
                exception.Message));
            solution = workspace.CurrentSolution;
        }

        var diagnostics = WorkspaceDiagnosticRegistry.Get(workspace);

        // Test overrides can return a solution owned by a different workspace. Such a solution
        // has no receipts from this load and must remain unverified rather than inheriting them.
        if (ReferenceEquals(solution.Workspace, workspace))
        {
            var completed = await WorkspaceAcquisitionCollector.CompleteAsync(
                solution,
                discovery,
                workspaceIdentity,
                inputFingerprint,
                preLoadInputSet,
                discoveryWindowStable,
                cancellationToken);
            WorkspaceAcquisitionRegistry.RefineDeclaration(
                workspace,
                discovery.Manifest,
                completed.Manifest,
                completed.Provenance);
            WorkspaceAcquisitionRegistry.Attach(workspace, new WorkspaceAcquisitionReceipt(
                solution,
                completed.Manifest,
                completed.Outcomes,
                completed.Provenance));
        }
        EmitDiagnostics(diagnostics, false);
        return (workspace, solution);
    }

    private static void EmitDiagnostics(IReadOnlyList<WorkspaceDiagnostic> diagnostics, bool fatal)
    {
        if (diagnostics.Count == 0)
            return;

        // Suppress warnings by default; failures are always shown. --show-workspace-warnings opts back in.
        if (!fatal && !RtContext.ShowWorkspaceWarnings)
            return;

        var legacySink = DiagnosticSinkOverride.Value;
        if (legacySink is not null)
        {
            legacySink.WriteLine("---");
            legacySink.WriteLine(fatal ? "[workspace-failures]" : "[workspace-warnings]");
            foreach (var d in diagnostics)
                legacySink.WriteLine($"  {d.Kind}: {d.Message}");
            return;
        }

        if (fatal)
        {
            RtLog.Error("---");
            RtLog.Error("[workspace-failures]");
            foreach (var d in diagnostics)
                RtLog.Error("  {Kind}: {Message}", d.Kind, d.Message);
        }
        else
        {
            RtLog.Warning("---");
            RtLog.Warning("[workspace-warnings]");
            foreach (var d in diagnostics)
                RtLog.Warning("  {Kind}: {Message}", d.Kind, d.Message);
        }
    }

    private sealed class OverrideScope : IDisposable
    {
        public void Dispose()
        {
            Override.Value = null;
        }
    }
}
