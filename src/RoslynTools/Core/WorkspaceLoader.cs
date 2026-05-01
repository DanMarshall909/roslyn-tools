using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynTools.Core;

internal static class WorkspaceLoader
{
    /// <summary>
    /// Test hook: if set, <see cref="LoadAsync"/> returns this pair instead of opening MSBuild.
    /// AsyncLocal so parallel xunit test methods don't clobber each other. Never read or written by production code.
    /// </summary>
    internal static AsyncLocal<(Workspace Workspace, Solution Solution)?> Override { get; } = new();

    /// <summary>
    /// Test hook: redirects diagnostic output away from <see cref="Console.Error"/> when set.
    /// AsyncLocal so parallel xunit tests don't clobber each other.
    /// </summary>
    internal static AsyncLocal<TextWriter?> DiagnosticSinkOverride { get; } = new();

    /// <summary>
    /// Test-only scope: installs an override for the duration of the returned IDisposable, then clears it.
    /// </summary>
    internal static IDisposable UseOverride(Workspace workspace, Solution solution)
    {
        Override.Value = (workspace, solution);
        return new OverrideScope();
    }

    private sealed class OverrideScope : IDisposable
    {
        public void Dispose() => Override.Value = null;
    }

    internal static async Task<(Workspace Workspace, Solution Solution)> LoadAsync(string? solutionPath)
    {
        if (Override.Value is { } o)
            return (o.Workspace, o.Solution);

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
        var subscription = workspace.RegisterWorkspaceFailedHandler(e =>
        {
            lock (diagnostics) diagnostics.Add(e.Diagnostic);
        });

        Solution solution;
        try
        {
            solution = await workspace.OpenSolutionAsync(solutionPath);
        }
        catch
        {
            EmitDiagnostics(diagnostics, fatal: true);
            workspace.Dispose();
            throw;
        }

        // Fail fast only when we actually got nothing usable: no projects AND a Failure-kind diagnostic.
        // Warnings (restore, package audit, unresolved imports) are non-fatal as long as Roslyn has a usable model.
        var hasFailureKind = diagnostics.Any(d => d.Kind == WorkspaceDiagnosticKind.Failure);
        if (!solution.Projects.Any() && hasFailureKind)
        {
            EmitDiagnostics(diagnostics, fatal: true);
            workspace.Dispose();
            throw new InvalidOperationException(
                $"Solution '{solutionPath}' failed to load: no projects and {diagnostics.Count} diagnostic(s). See [workspace-warnings] above.");
        }

        EmitDiagnostics(diagnostics, fatal: false);
        return (workspace, solution);
    }

    private static void EmitDiagnostics(List<WorkspaceDiagnostic> diagnostics, bool fatal)
    {
        if (diagnostics.Count == 0)
            return;

        // Legacy test-hook: if DiagnosticSinkOverride is set, write there directly.
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

        for (DirectoryInfo? directory = currentDirectory; directory is not null; directory = directory.Parent)
        {
            var candidates = Directory
                .EnumerateFiles(directory.FullName, "*.sln", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count == 1)
                return candidates[0];

            if (candidates.Count > 1)
            {
                var names = string.Join(", ", candidates.Select(Path.GetFileName));
                throw new InvalidOperationException(
                    $"Multiple .sln files found in '{directory.FullName}' ({names}). Pass --solution explicitly.");
            }
        }

        throw new FileNotFoundException(
            "No .sln file was discovered while searching from the current directory to the filesystem root. Pass --solution explicitly to override.");
    }

    private static string ResolveExplicitSolutionPath(string solutionPath)
    {
        return Path.GetFullPath(solutionPath);
    }

    private static string? ResolveRepoDefaultPath(DirectoryInfo currentDirectory)
    {
        for (DirectoryInfo? directory = currentDirectory; directory is not null; directory = directory.Parent)
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

        if (!resolvedPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Repository solution default '.rt-solution' in '{repoRoot}' points to '{configuredPath}', which is not a .sln file.");

        if (!File.Exists(resolvedPath))
            throw new InvalidOperationException(
                $"Repository solution default '.rt-solution' in '{repoRoot}' points to '{configuredPath}', which does not exist.");
    }

}
