using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynTools.Core;

internal static class WorkspaceLoader
{
    internal static async Task<(MSBuildWorkspace Workspace, Solution Solution)> LoadAsync(string? solutionPath)
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

        // Log workspace diagnostics as warnings rather than throwing — packages.config
        // projects produce many "cannot find file" warnings but load correctly.
        workspace.WorkspaceFailed += (_, e) =>
            Console.Error.WriteLine($"[workspace] {e.Diagnostic.Kind}: {e.Diagnostic.Message}");

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
            .EnumerateFiles(cwd, "*.slnx", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(cwd, "*.sln", SearchOption.TopDirectoryOnly))
            .ToList();

        if (candidates.Count == 1)
            return candidates[0];

        if (candidates.Count == 0)
            throw new FileNotFoundException(
                "No .sln or .slnx file found in current directory. Pass --solution explicitly.");

        var names = string.Join(", ", candidates.Select(Path.GetFileName));
        throw new InvalidOperationException(
            $"Multiple solution files found ({names}). Pass --solution explicitly.");
    }
}
