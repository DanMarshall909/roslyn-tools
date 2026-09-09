namespace RoslynTools.Core;

/// <summary>
/// Resolves the effective solution path without loading or evaluating repository build inputs.
/// </summary>
internal static class SolutionPathResolver
{
    internal static string Resolve(string? solutionPath)
    {
        if (!string.IsNullOrWhiteSpace(solutionPath))
            return Path.GetFullPath(solutionPath);

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
}
