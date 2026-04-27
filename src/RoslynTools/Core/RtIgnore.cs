using System.IO.Enumeration;
using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

internal sealed class RtIgnore
{
    private static readonly RtIgnore Empty = new([]);

    private readonly string[] _patterns;

    private RtIgnore(string[] patterns) => _patterns = patterns;

    internal static RtIgnore Load(string? solutionDir)
    {
        if (string.IsNullOrWhiteSpace(solutionDir))
            return Empty;

        var path = Path.Combine(solutionDir, ".rtignore");
        if (!File.Exists(path))
            return Empty;

        var patterns = File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToArray();

        return patterns.Length == 0 ? Empty : new RtIgnore(patterns);
    }

    internal static RtIgnore ForSolution(Solution solution)
    {
        var solutionDir = Path.GetDirectoryName(solution.FilePath);
        if (!string.IsNullOrWhiteSpace(solutionDir))
            return Load(solutionDir);

        return Load(InferSolutionDirectory(solution));
    }

    private static string? InferSolutionDirectory(Solution solution)
    {
        var projectDirectories = solution.Projects
            .Select(p => p.FilePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetDirectoryName(p!))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToList();

        if (projectDirectories.Count == 0)
            return null;

        var common = projectDirectories[0];
        foreach (var directory in projectDirectories.Skip(1))
            common = GetCommonDirectory(common, directory);

        return common;
    }

    private static string GetCommonDirectory(string left, string right)
    {
        var leftParts = Path.GetFullPath(left)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rightParts = Path.GetFullPath(right)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var count = Math.Min(leftParts.Length, rightParts.Length);
        var commonParts = new List<string>();
        for (var i = 0; i < count; i++)
        {
            if (!string.Equals(leftParts[i], rightParts[i], StringComparison.OrdinalIgnoreCase))
                break;

            commonParts.Add(leftParts[i]);
        }

        if (commonParts.Count == 0)
            return Path.GetPathRoot(left) ?? left;

        return string.Join(Path.DirectorySeparatorChar, commonParts);
    }

    internal bool IsIgnored(string? absoluteFilePath)
    {
        if (absoluteFilePath is null || _patterns.Length == 0)
            return false;

        // Normalise to forward slashes for consistent matching.
        var normalised = absoluteFilePath.Replace('\\', '/');

        foreach (var raw in _patterns)
        {
            // Normalise pattern too.
            var pattern = raw.Replace('\\', '/').TrimEnd('/');

            // Plain segment match — e.g. "obj" matches any path containing /obj/
            if (!pattern.Contains('/') && !pattern.Contains('*'))
            {
                if (normalised.Contains($"/{pattern}/", StringComparison.OrdinalIgnoreCase) ||
                    normalised.Contains($"/{pattern}.", StringComparison.OrdinalIgnoreCase))
                    return true;
                continue;
            }

            // For patterns ending with / (directories), match as prefix on the normalised path.
            // Re-add the trailing slash we stripped to detect intent.
            if (raw.TrimEnd().EndsWith('/'))
            {
                if (normalised.Contains($"/{pattern}/", StringComparison.OrdinalIgnoreCase))
                    return true;
                continue;
            }

            // Glob match against the full normalised path and the filename.
            var fileName = Path.GetFileName(normalised);
            if (FileSystemName.MatchesSimpleExpression(pattern, normalised, ignoreCase: true) ||
                FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true))
                return true;
        }

        return false;
    }
}
