using System.Diagnostics;
using System.Text;

namespace RoslynTools.Services.SemanticDiff.Git;

/// <summary>
///     The real <see cref="IGitClient" />: shells the <c>git</c> CLI via
///     <see cref="Process" />, scoped to a single working directory chosen at
///     construction. Repository / ref validation throws; content reads return
///     <c>null</c> for an absent path (see <see cref="IGitClient" /> for the contract).
/// </summary>
internal sealed class GitClient : IGitClient
{
    private readonly string _workingDirectory;

    public GitClient(string workingDirectory) =>
        _workingDirectory = workingDirectory
            ?? throw new ArgumentNullException(nameof(workingDirectory));

    public void EnsureRepository()
    {
        if (Run("rev-parse", "--git-dir").ExitCode != 0)
            throw new InvalidOperationException($"not a git repository: {_workingDirectory}");
    }

    public void EnsureRefExists(string reference)
    {
        ArgumentException.ThrowIfNullOrEmpty(reference);
        if (Run("rev-parse", "--verify", $"{reference}^{{commit}}").ExitCode != 0)
            throw new ArgumentException($"invalid git ref: {reference}");
    }

    public IReadOnlyList<string> GetChangedPathsBetween(string fromRef, string toRef)
    {
        var result = Run("diff", "--no-renames", "--name-only", fromRef, toRef);
        return SplitLines(result.StdOut);
    }

    public IReadOnlyList<string> GetWorkingTreeChangedPaths()
    {
        var tracked = SplitLines(Run("diff", "--no-renames", "--name-only", "HEAD").StdOut);
        var untracked = SplitLines(Run("ls-files", "--others", "--exclude-standard").StdOut);

        // Dedup while preserving discovery order (tracked first, then untracked).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var union = new List<string>(tracked.Count + untracked.Count);
        foreach (var path in tracked.Concat(untracked))
            if (seen.Add(path))
                union.Add(path);

        return union;
    }

    public string? ReadFileAtRef(string reference, string path)
    {
        var result = Run("show", $"{reference}:{path}");
        // A nonzero exit means the path did not exist at that ref — return null, do not
        // throw: only repository / ref validation is an error.
        return result.ExitCode == 0 ? result.StdOut : null;
    }

    public string? ReadWorkingTreeFile(string path)
    {
        var full = Path.Combine(_workingDirectory, path);
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    private static List<string> SplitLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static l => l.TrimEnd('\r'))
            .Where(static l => l.Length > 0)
            .ToList();

    private (int ExitCode, string StdOut) Run(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = _workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("failed to start git process");

        // Read stdout to completion before WaitForExit to avoid a pipe-buffer deadlock;
        // drain stderr too so a chatty git can't block on its error pipe.
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        return (proc.ExitCode, stdout);
    }
}
