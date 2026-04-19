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

        // Log workspace diagnostics as warnings rather than throwing — packages.config
        // projects produce many "cannot find file" warnings but load correctly.
        workspace.RegisterWorkspaceFailedHandler(e =>
            Console.Error.WriteLine($"[workspace] {e.Diagnostic.Kind}: {e.Diagnostic.Message}"));

        Console.Error.WriteLine($"Loading solution: {solutionPath}");
        var solution = await workspace.OpenSolutionAsync(solutionPath);
        Console.Error.WriteLine($"Loaded {solution.Projects.Count()} project(s).");

        return (workspace, solution);
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
