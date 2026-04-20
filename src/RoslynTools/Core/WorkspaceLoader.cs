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

    private static void EmitDiagnostics(IReadOnlyList<WorkspaceDiagnostic> diagnostics, bool fatal)
    {
        if (diagnostics.Count == 0)
            return;

        var sink = DiagnosticSinkOverride.Value ?? Console.Error;
        sink.WriteLine("---");
        sink.WriteLine(fatal ? "[workspace-failures]" : "[workspace-warnings]");
        foreach (var d in diagnostics)
            sink.WriteLine($"  {d.Kind}: {d.Message}");
    }

    private static string ResolveSolutionPath(string? solutionPath)
    {
        if (!string.IsNullOrWhiteSpace(solutionPath))
            return Path.GetFullPath(solutionPath);

        var cwd = Directory.GetCurrentDirectory();
        var candidates = Directory
            .EnumerateFiles(cwd, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(cwd, "*.slnx", SearchOption.TopDirectoryOnly))
            .ToList();

        if (candidates.Count == 1)
            return candidates[0];

        if (candidates.Count == 0)
            throw new FileNotFoundException(
                "No .sln/.slnx file found in current directory. Pass --solution explicitly.");

        // Prefer .sln over .slnx when both exist for the same base name.
        var deduplicated = candidates
            .GroupBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.FirstOrDefault(p => p.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)) ?? g.First())
            .ToList();

        if (deduplicated.Count == 1)
            return deduplicated[0];

        var names = string.Join(", ", deduplicated.Select(Path.GetFileName));
        throw new InvalidOperationException(
            $"Multiple solution files found ({names}). Pass --solution explicitly.");
    }
}
