namespace RoslynTools.Core;

/// <summary>
/// Resolves the effective solution path without loading or evaluating repository build inputs.
/// </summary>
internal static class SolutionPathResolver
{
    internal static string Resolve(string? solutionPath)
    {
        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            if (!Path.IsPathFullyQualified(solutionPath)
                && RepositoryTrustContext.UsesPersistentTrust)
            {
                throw new InvalidOperationException(
                    "A persistent multi-repository host requires an absolute solution path.");
            }

            var explicitRoot = RepositoryTrustContext.TrustedRepositoryRoot;
            var resolved = Path.IsPathFullyQualified(solutionPath)
                ? Path.GetFullPath(solutionPath)
                : Path.GetFullPath(solutionPath, explicitRoot ?? Directory.GetCurrentDirectory());
            ValidateTrustedRoot(explicitRoot, resolved);
            return resolved;
        }

        if (RepositoryTrustContext.UsesPersistentTrust)
        {
            throw new InvalidOperationException(
                "A persistent multi-repository host requires an absolute solution path.");
        }

        var trustedRoot = RepositoryTrustContext.TrustedRepositoryRoot;
        var startDirectory = new DirectoryInfo(trustedRoot ?? Directory.GetCurrentDirectory());
        var configuredDefault = ResolveRepoDefaultPath(startDirectory, trustedRoot);
        if (configuredDefault is not null)
            return configuredDefault;

        for (var directory = startDirectory; directory is not null; directory = directory.Parent)
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

            if (trustedRoot is not null)
                break;
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

    private static string? ResolveRepoDefaultPath(DirectoryInfo currentDirectory, string? trustedRoot)
    {
        for (var directory = currentDirectory; directory is not null; directory = directory.Parent)
        {
            var repoDefaultPath = Path.Combine(directory.FullName, ".rt-solution");
            if (File.Exists(repoDefaultPath))
            {
                var configuredPath = File.ReadAllText(repoDefaultPath).Trim();
                var resolvedPath = Path.GetFullPath(configuredPath, directory.FullName);
                ValidateRepoDefaultTarget(directory.FullName, configuredPath, resolvedPath);
                ValidateTrustedRoot(trustedRoot, resolvedPath);
                return resolvedPath;
            }

            if (trustedRoot is not null)
                break;
        }

        return null;
    }

    private static void ValidateTrustedRoot(string? trustedRoot, string resolvedPath)
    {
        if (trustedRoot is null)
            return;

        var canonicalRoot = RepositoryTrustRegistry.CanonicalizeExistingPath(trustedRoot);
        var canonicalPath = RepositoryTrustRegistry.CanonicalizeExistingPath(resolvedPath);
        if (!RepositoryTrustRegistry.IsContained(canonicalRoot, canonicalPath))
            throw RepositoryTrustRequiredException.ForSolution(resolvedPath);
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
