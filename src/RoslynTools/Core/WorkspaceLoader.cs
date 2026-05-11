using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynTools.Core;

/// <summary>
/// Loads and manages workspaces for solution processing.
/// </summary>
internal sealed class WorkspaceLoader : IDisposable
{
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
    ///     is disposed (i.e. when the DI container tears down).
    /// </summary>
    internal async Task<Solution> GetSolutionAsync(string? solutionPath)
    {
        if (Override.Value is { } o)
            return o.Solution;

        var (workspace, solution) = await LoadCoreAsync(solutionPath);
        _workspace?.Dispose();
        _workspace = workspace;
        return solution;
    }

    // ── Static API (backward-compat for not-yet-migrated commands) ──────────

    /// <summary>
    ///     Loads a fresh workspace and solution. Caller owns the returned workspace and must
    ///     dispose it. Prefer the DI-injected <see cref="WorkspaceLoader" /> and
    ///     <see cref="GetSolutionAsync" /> for new code.
    /// </summary>
    internal static async Task<(Workspace Workspace, Solution Solution)> LoadAsync(string? solutionPath)
    {
        if (Override.Value is { } o)
            return (o.Workspace, o.Solution);

        var (workspace, solution) = await LoadCoreAsync(solutionPath);
        return (workspace, solution);
    }

    // ── Shared load logic ───────────────────────────────────────────────────

    private static async Task<(MSBuildWorkspace Workspace, Solution Solution)> LoadCoreAsync(string? solutionPath)
    {
        solutionPath = ResolveSolutionPath(solutionPath);

        if (!File.Exists(solutionPath))
            throw new FileNotFoundException($"Solution not found: {solutionPath}");

        var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
        {
            // Essential for legacy web projects that import TypeScript/EF Design targets
            // which may not be present in all environments.
            { "SkipUnrecognizedProjects", "true" }
        });

        var diagnostics = new List<WorkspaceDiagnostic>();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            lock (diagnostics)
            {
                diagnostics.Add(e.Diagnostic);
            }
        });

        Solution solution;
        try
        {
            solution = await workspace.OpenSolutionAsync(solutionPath);
        }
        catch
        {
            EmitDiagnostics(diagnostics, true);
            workspace.Dispose();
            throw;
        }

        // Fail fast only when we actually got nothing usable: no projects AND a Failure-kind diagnostic.
        // Warnings (restore, package audit, unresolved imports) are non-fatal as long as Roslyn has a usable model.
        var hasFailureKind = diagnostics.Any(d => d.Kind == WorkspaceDiagnosticKind.Failure);
        if (!solution.Projects.Any() && hasFailureKind)
        {
            EmitDiagnostics(diagnostics, true);
            workspace.Dispose();
            throw new InvalidOperationException(
                $"Solution '{solutionPath}' failed to load: no projects and {diagnostics.Count} diagnostic(s). See [workspace-warnings] above.");
        }

        EmitDiagnostics(diagnostics, false);
        return (workspace, solution);
    }

    private static void EmitDiagnostics(List<WorkspaceDiagnostic> diagnostics, bool fatal)
    {
        if (diagnostics.Count == 0)
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

    private static string ResolveSolutionPath(string? solutionPath)
    {
        if (!string.IsNullOrWhiteSpace(solutionPath))
            return ResolveExplicitSolutionPath(solutionPath);

        var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
        var configuredDefault = ResolveRepoDefaultPath(currentDirectory);
        if (configuredDefault is not null)
            return configuredDefault;

        for (var directory = currentDirectory; directory is not null; directory = directory.Parent)
        {
            var candidates = EnumerateSolutionFiles(directory.FullName)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count == 1)
                return candidates[0];

            if (candidates.Count > 1)
            {
                var names = string.Join(", ", candidates.Select(Path.GetFileName));
                throw new InvalidOperationException(
                    $"Multiple .sln/.slnx files found in '{directory.FullName}' ({names}). Pass --solution explicitly.");
            }
        }

        throw new FileNotFoundException(
            "No .sln/.slnx file was discovered while searching from the current directory to the filesystem root. Pass --solution explicitly to override.");
    }

    private static IEnumerable<string> EnumerateSolutionFiles(string directory)
    {
        foreach (var solution in Directory.EnumerateFiles(directory, "*.sln", SearchOption.TopDirectoryOnly))
            yield return solution;

        foreach (var solution in Directory.EnumerateFiles(directory, "*.slnx", SearchOption.TopDirectoryOnly))
            yield return solution;
    }

    private static string ResolveExplicitSolutionPath(string solutionPath)
    {
        return Path.GetFullPath(solutionPath);
    }

    private static string? ResolveRepoDefaultPath(DirectoryInfo currentDirectory)
    {
        for (var directory = currentDirectory; directory is not null; directory = directory.Parent)
        {
            var repoDefaultPath = Path.Combine(directory.FullName, ".rt-solution");
            if (!File.Exists(repoDefaultPath))
                continue;

            var configuredPath = File.ReadAllText(repoDefaultPath).Trim();
            var resolvedPath = Path.GetFullPath(configuredPath, directory.FullName);
            ValidateRepoDefaultTarget(directory.FullName, configuredPath, resolvedPath);
            return resolvedPath;
        }

        return null;
    }

    private static void ValidateRepoDefaultTarget(string repoRoot, string configuredPath, string resolvedPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new InvalidOperationException(
                $"Repository solution default '.rt-solution' in '{repoRoot}' is empty. Pass --solution explicitly.");

        if (!resolvedPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            && !resolvedPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Repository solution default '.rt-solution' in '{repoRoot}' points to '{configuredPath}', which is not a .sln/.slnx file.");

        if (!File.Exists(resolvedPath))
            throw new InvalidOperationException(
                $"Repository solution default '.rt-solution' in '{repoRoot}' points to '{configuredPath}', which does not exist.");
    }

    private sealed class OverrideScope : IDisposable
    {
        public void Dispose()
        {
            Override.Value = null;
        }
    }
}
