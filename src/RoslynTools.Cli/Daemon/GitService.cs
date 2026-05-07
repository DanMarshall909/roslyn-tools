namespace RoslynTools.Daemon;

internal sealed class GitService
{
    internal GitState GetState(string workingDirectory)
    {
        try
        {
            var gitDir = FindGitDir(workingDirectory);
            if (gitDir is null) return new GitState("unknown", false, null);

            var head = ResolveHead(gitDir);
            var indexMtime = new FileInfo(Path.Combine(gitDir, "index")).LastWriteTimeUtc.ToFileTimeUtc();
            return new GitState(head, true, indexMtime);
        }
        catch
        {
            return new GitState("unknown", false, null);
        }
    }

    private static string? FindGitDir(string start)
    {
        for (var dir = start; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, ".git");
            if (Directory.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static string ResolveHead(string gitDir)
    {
        var headContent = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
        if (headContent.StartsWith("ref: ", StringComparison.Ordinal))
        {
            var refPath = Path.Combine(gitDir, headContent[5..].Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(refPath)) return File.ReadAllText(refPath).Trim();
        }

        return headContent;
    }
}
