using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using RoslynTools.Core;

namespace RoslynTools.Services.SemanticDiff.Git;

/// <summary>
///     The real <see cref="IGitClient" />: shells the <c>git</c> CLI via
///     <see cref="Process" />, scoped to a single working directory chosen at
///     construction. Repository / ref validation throws; content reads return
///     <c>null</c> for a proven absent path and throw on Git failure
///     (see <see cref="IGitClient" /> for the contract).
/// </summary>
internal sealed class GitClient : IGitClient
{
    private readonly string _workingDirectory;
    private readonly string _gitExecutable;
    private readonly Lazy<string> _repositoryRoot;
    private readonly bool _containedWorkingTreeReadsAvailable;

    public GitClient(string workingDirectory) : this(workingDirectory, "git")
    {
    }

    internal GitClient(
        string workingDirectory,
        string gitExecutable,
        bool? containedWorkingTreeReadsAvailable = null)
    {
        _workingDirectory = workingDirectory
            ?? throw new ArgumentNullException(nameof(workingDirectory));
        _gitExecutable = gitExecutable
            ?? throw new ArgumentNullException(nameof(gitExecutable));
        _repositoryRoot = new Lazy<string>(ReadRepositoryRoot);
        _containedWorkingTreeReadsAvailable = containedWorkingTreeReadsAvailable
            ?? WorkingTreeReadCapability.IsAvailable;
    }

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

    public void EnsureWorkingTreeContentSupported()
    {
        if (_containedWorkingTreeReadsAvailable)
            return;
        const string message = "Repository-contained working-tree semantic diff is unsupported on this platform; "
            + "compare two Git refs or two files instead.";
        throw new GitContentReadException("WORKTREE", string.Empty, message,
            new PlatformNotSupportedException(message));
    }

    public IReadOnlyList<string> GetChangedPathsBetween(string fromRef, string toRef)
    {
        var result = Run("diff", "--no-renames", "--name-only", "-z", fromRef, toRef);
        EnsureSuccess(result, $"diff --no-renames --name-only -z {fromRef} {toRef}");
        return SplitPaths(result.StdOut);
    }

    public IReadOnlyList<string> GetWorkingTreeChangedPaths()
    {
        var trackedResult = Run("diff", "--no-renames", "--name-only", "-z", "HEAD");
        EnsureSuccess(trackedResult, "diff --no-renames --name-only -z HEAD");
        var untrackedResult = Run("ls-files", "--others", "--exclude-standard", "--full-name", "-z");
        EnsureSuccess(untrackedResult, "ls-files --others --exclude-standard --full-name -z");
        var tracked = SplitPaths(trackedResult.StdOut);
        var untracked = SplitPaths(untrackedResult.StdOut);

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
        try
        {
            // Git show can return nonzero for either an absent path or an unreadable
            // object, and can even return success with empty output for an unmatched
            // pathspec. Establish exact membership before reading the content.
            // The changed-path APIs return root-relative names.
            var tree = Run(
                "ls-tree", "-r", "-z", "--name-only", "--full-tree",
                reference, "--", $":(literal){path}");
            EnsureSuccess(tree, $"ls-tree {reference} -- {path}");
            if (!tree.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Contains(path, StringComparer.Ordinal))
                return null;

            var content = Run("show", $"{reference}:{path}");
            EnsureSuccess(content, $"show {reference}:{path}");
            return content.StdOut;
        }
        catch (InvalidOperationException exception)
        {
            throw new GitContentReadException(reference, path, exception.Message, exception);
        }
    }

    public string? ReadWorkingTreeFile(string path)
    {
        EnsureWorkingTreeContentSupported();
        try
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path) || path.Contains('\0'))
                throw new InvalidOperationException("A repository-relative file path is required.");

            var components = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (components.Any(component => component is "" or "." or ".."))
                throw new InvalidOperationException("The working-tree path contains an invalid component.");

            using var handle = LinuxStagingHandles.TryOpenContainedReadOnlyFile(
                _repositoryRoot.Value, components);
            if (handle is null)
                return null;
            using var stream = new FileStream(handle, FileAccess.Read);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new GitContentReadException("WORKTREE", path,
                $"Could not read working-tree file '{path}': {exception.Message}", exception);
        }
    }

    private static List<string> SplitPaths(string text) =>
        text.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();

    private string ReadRepositoryRoot()
    {
        var result = Run("rev-parse", "--show-toplevel");
        EnsureSuccess(result, "rev-parse --show-toplevel");
        var root = result.StdOut.TrimEnd('\r', '\n');
        if (root.Length == 0)
            throw new InvalidOperationException(
                $"git rev-parse --show-toplevel returned no repository root in '{_workingDirectory}'");
        return root;
    }

    private void EnsureSuccess((int ExitCode, string StdOut, string StdErr) result, string command)
    {
        if (result.ExitCode != 0)
        {
            var detail = result.StdErr.Trim();
            if (detail.Length > 512)
                detail = detail[..512] + "…";
            detail = detail.Replace('\r', ' ').Replace('\n', ' ');
            throw new InvalidOperationException(
                $"git {command} failed in '{_workingDirectory}' (exit {result.ExitCode}): {detail}");
        }
    }

    private (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
    {
        var psi = new ProcessStartInfo(_gitExecutable)
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

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException("failed to start git process");
        }
        catch (Exception exception) when (exception is Win32Exception or DirectoryNotFoundException)
        {
            throw new InvalidOperationException(
                $"Could not start git in '{_workingDirectory}': {exception.Message}", exception);
        }

        using var proc = process;

        // Drain both redirected pipes concurrently so a full stderr pipe cannot
        // block git while the parent waits for stdout to close.
        var stdout = string.Empty;
        var stderr = string.Empty;
        Parallel.Invoke(
            () => stdout = proc.StandardOutput.ReadToEnd(),
            () => stderr = proc.StandardError.ReadToEnd());
        proc.WaitForExit();

        return (proc.ExitCode, stdout, stderr);
    }
}
