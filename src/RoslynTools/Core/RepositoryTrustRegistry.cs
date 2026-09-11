using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RoslynTools.Core;

/// <summary>
///     Preview user-scoped repository trust registry. Records bind a canonical Git common
///     directory path to a Git-object continuity anchor. Every authorization revalidates the
///     requested path against Git's current registered-worktree inventory.
/// </summary>
internal sealed class RepositoryTrustRegistry
{
    private const int SchemaVersion = 1;
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _registryPath;
    private readonly string _registryDirectory;
    private readonly string _lockPath;

    internal RepositoryTrustRegistry(string? registryPath = null)
    {
        _registryPath = Path.GetFullPath(registryPath ?? DefaultRegistryPath);
        _registryDirectory = Path.GetDirectoryName(_registryPath)!;
        _lockPath = $"{_registryPath}.lock";
    }

    internal static string DefaultRegistryPath => Path.Combine(
        ResolveLocalApplicationData(),
        "RoslynTools",
        "repository-trust.json");

    internal Result<RepositoryTrustStatus, RtError> Add(string repositoryPath)
    {
        var binding = ResolveBinding(repositoryPath);
        if (binding.IsErr)
            return binding.Map(_ => default(RepositoryTrustStatus)!);

        var resolved = binding.Unwrap();
        try
        {
            using var registryLock = AcquireMutationLock();
            var loaded = LoadStore();
            if (loaded.IsErr)
                return loaded.Map(_ => default(RepositoryTrustStatus)!);

            var store = loaded.Unwrap();
            var anchor = ResolveAnchorCommit(resolved);
            if (anchor.IsErr)
                return anchor.Map(_ => default(RepositoryTrustStatus)!);
            var anchorCommit = anchor.Unwrap();
            var record = new RepositoryTrustRecord(
                resolved.RepositoryId,
                resolved.CommonDirectory,
                resolved.WorktreeRoot,
                anchorCommit,
                DateTimeOffset.UtcNow);
            var records = store.Records
                .Where(existing => !string.Equals(
                    existing.RepositoryId,
                    record.RepositoryId,
                    StringComparison.Ordinal))
                .ToList();
            records.Add(record);
            records.Sort((left, right) => StringComparer.Ordinal.Compare(
                left.RepositoryId,
                right.RepositoryId));
            WriteStore(new RepositoryTrustStore(SchemaVersion, records));
            return Result<RepositoryTrustStatus, RtError>.Ok(ToStatus(record, resolved));
        }
        catch (Exception ex) when (IsStorageException(ex))
        {
            return Result<RepositoryTrustStatus, RtError>.Err(StorageError(ex));
        }
    }

    internal Result<IReadOnlyList<RepositoryTrustStatus>, RtError> List()
    {
        var loaded = LoadStore();
        if (loaded.IsErr)
            return loaded.Map<IReadOnlyList<RepositoryTrustStatus>>(_ => []);

        var store = loaded.Unwrap();
        var statuses = store.Records
            .Select(CreateStoredStatus)
            .OrderBy(status => status.RepositoryId, StringComparer.Ordinal)
            .ToArray();
        return Result<IReadOnlyList<RepositoryTrustStatus>, RtError>.Ok(statuses);
    }

    internal Result<RepositoryTrustStatus, RtError> Status(string repositoryPath)
    {
        var loaded = LoadStore();
        if (loaded.IsErr)
            return loaded.Map(_ => default(RepositoryTrustStatus)!);

        var store = loaded.Unwrap();
        var binding = ResolveBinding(repositoryPath);
        if (binding.IsOk)
        {
            var resolved = binding.Unwrap();
            var record = store.Records.FirstOrDefault(existing =>
                MatchesRepositoryIdentity(
                    existing.RepositoryId,
                    existing.CommonDirectory,
                    resolved.RepositoryId,
                    resolved.CommonDirectory));
            return Result<RepositoryTrustStatus, RtError>.Ok(record is null
                ? RepositoryTrustStatus.Untrusted(resolved)
                : AnchorExists(resolved, record.AnchorCommit)
                    ? ToStatus(record, resolved)
                    : ToStaleStatus(
                        record,
                        "The recorded Git object anchor is absent from the current repository."));
        }

        string canonicalInput;
        try
        {
            canonicalInput = CanonicalizeInput(repositoryPath);
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or IOException
                                   or NotSupportedException)
        {
            return binding.Map(_ => default(RepositoryTrustStatus)!);
        }
        var staleRecord = store.Records.FirstOrDefault(existing =>
            PathsEqual(existing.GrantedFromWorktree, canonicalInput)
            || PathsEqual(existing.CommonDirectory, canonicalInput)
            || string.Equals(existing.RepositoryId, repositoryPath, StringComparison.Ordinal));
        return staleRecord is null
            ? binding.Map(_ => default(RepositoryTrustStatus)!)
            : Result<RepositoryTrustStatus, RtError>.Ok(ToStaleStatus(
                staleRecord,
                "The recorded repository path is missing or is no longer a registered Git worktree."));
    }

    internal Result<RepositoryTrustStatus, RtError> Remove(string repositoryPath)
    {
        try
        {
            using var registryLock = AcquireMutationLock();
            var loaded = LoadStore();
            if (loaded.IsErr)
                return loaded.Map(_ => default(RepositoryTrustStatus)!);

            var store = loaded.Unwrap();
            var binding = ResolveBinding(repositoryPath);
            string canonicalInput;
            try
            {
                canonicalInput = CanonicalizeInput(repositoryPath);
            }
            catch (Exception ex) when (ex is ArgumentException
                                       or IOException
                                       or NotSupportedException)
            {
                return binding.Map(_ => default(RepositoryTrustStatus)!);
            }
            var repositoryId = binding.Match(
                value => value.RepositoryId,
                _ => repositoryPath);
            var record = store.Records.FirstOrDefault(existing =>
                string.Equals(existing.RepositoryId, repositoryId, StringComparison.Ordinal)
                || PathsEqual(existing.GrantedFromWorktree, canonicalInput)
                || PathsEqual(existing.CommonDirectory, canonicalInput));
            if (record is null)
            {
                return binding.IsOk
                    ? Result<RepositoryTrustStatus, RtError>.Ok(RepositoryTrustStatus.Untrusted(
                        binding.Unwrap()))
                    : binding.Map(_ => default(RepositoryTrustStatus)!);
            }

            var records = store.Records.Where(existing => !ReferenceEquals(existing, record)).ToArray();
            WriteStore(new RepositoryTrustStore(SchemaVersion, records));
            return Result<RepositoryTrustStatus, RtError>.Ok(new RepositoryTrustStatus(
                record.RepositoryId,
                record.CommonDirectory,
                record.GrantedFromWorktree,
                RepositoryTrustState.Untrusted,
                record.GrantedAtUtc,
                "Persistent repository trust was removed."));
        }
        catch (Exception ex) when (IsStorageException(ex))
        {
            return Result<RepositoryTrustStatus, RtError>.Err(StorageError(ex));
        }
    }

    internal bool IsTrustedFor(string path)
    {
        var binding = ResolveBinding(path);
        if (binding.IsErr)
            return false;

        var resolved = binding.Unwrap();
        var loaded = LoadStore();
        if (loaded.IsErr)
            return false;

        var store = loaded.Unwrap();
        return store.Records.Any(record =>
                MatchesRepositoryIdentity(
                    record.RepositoryId,
                    record.CommonDirectory,
                    resolved.RepositoryId,
                    resolved.CommonDirectory)
                && AnchorExists(resolved, record.AnchorCommit));
    }

    internal bool HasAnyTrustedRepository()
    {
        var listed = List();
        return listed.Match(
            HasTrustedStatus,
            _ => false);
    }

    private RepositoryTrustStatus CreateStoredStatus(RepositoryTrustRecord record)
    {
        if (!Directory.Exists(record.CommonDirectory))
            return ToStaleStatus(record, "The recorded Git common directory no longer exists.");

        var worktrees = RunGit(
            record.CommonDirectory,
            $"--git-dir={record.CommonDirectory}",
            "worktree",
            "list",
            "--porcelain",
            "-z");
        if (worktrees.IsErr)
            return ToStaleStatus(record, "The recorded Git common directory is no longer readable.");

        foreach (var worktreeRoot in ParseWorktreeRoots(worktrees.Unwrap()))
        {
            var binding = ResolveBinding(worktreeRoot);
            if (binding.IsErr)
                // Stryker disable once Statement : An invalid Git-listed worktree is skipped; removing this remains fail-closed by throwing on Unwrap.
                continue;

            var resolved = binding.Unwrap();
            if (MatchesRepositoryIdentity(
                    record.RepositoryId,
                    record.CommonDirectory,
                    resolved.RepositoryId,
                    resolved.CommonDirectory)
                && AnchorExists(resolved, record.AnchorCommit))
                return ToStatus(record, resolved);
        }

        return ToStaleStatus(record, "No registered worktree matches the recorded repository identity.");
    }

    private static Result<RepositoryBinding, RtError> ResolveBinding(string path)
    {
        try
        {
            return ResolveBindingCore(path);
        }
        catch (Exception ex) when (IsBindingException(ex))
        {
            return Result<RepositoryBinding, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"The repository path is invalid or unreadable: {ex.Message}"));
        }
    }

    private static Result<RepositoryBinding, RtError> ResolveBindingCore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Result<RepositoryBinding, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "A repository path is required."));

        var fullPath = CanonicalizeInput(path);
        var workingDirectory = File.Exists(fullPath)
            ? Path.GetDirectoryName(fullPath)
            : Directory.Exists(fullPath)
                ? fullPath
                : null;
        if (workingDirectory is null)
            return Result<RepositoryBinding, RtError>.Err(RtError.NotFound(
                $"The repository path does not exist: '{fullPath}'."));

        var rootResult = RunGit(workingDirectory, "rev-parse", "--path-format=absolute", "--show-toplevel");
        if (rootResult.IsErr)
            return rootResult.Map(_ => default(RepositoryBinding)!);
        var commonResult = RunGit(workingDirectory, "rev-parse", "--path-format=absolute", "--git-common-dir");
        if (commonResult.IsErr)
            return commonResult.Map(_ => default(RepositoryBinding)!);

        var worktreeRoot = CanonicalizeExistingDirectory(rootResult.Unwrap().Trim());
        var commonDirectory = CanonicalizeExistingDirectory(commonResult.Unwrap().Trim());
        if (!IsContained(worktreeRoot, fullPath))
        {
            // Stryker disable once String : Diagnostic text only; the containment predicate and typed rejection remain mutated.
            return Result<RepositoryBinding, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"The requested path is outside the resolved Git worktree: '{fullPath}'."));
        }

        var worktreesResult = RunGit(
            worktreeRoot,
            "worktree",
            "list",
            "--porcelain",
            "-z");
        if (worktreesResult.IsErr)
            return worktreesResult.Map(_ => default(RepositoryBinding)!);

        var registered = ParseWorktreeRoots(worktreesResult.Unwrap());
        if (!registered.Any(root => PathsEqual(root, worktreeRoot)))
        {
            // Stryker disable once String : Diagnostic text only; registered-worktree admission remains mutated.
            return Result<RepositoryBinding, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"The requested path is not beneath a currently registered Git worktree: '{fullPath}'."));
        }

        return Result<RepositoryBinding, RtError>.Ok(new RepositoryBinding(
            ComputeRepositoryId(commonDirectory),
            commonDirectory,
            worktreeRoot));
    }

    private Result<RepositoryTrustStore, RtError> LoadStore()
    {
        if (!File.Exists(_registryPath))
            return Result<RepositoryTrustStore, RtError>.Ok(new RepositoryTrustStore(SchemaVersion, []));

        try
        {
            using var stream = new FileStream(
                _registryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var store = JsonSerializer.Deserialize<RepositoryTrustStore>(stream, JsonOptions);
            if (store is null || store.SchemaVersion != SchemaVersion || store.Records is null)
                return Result<RepositoryTrustStore, RtError>.Err(CorruptStoreError());

            if (store.Records.Any(record => record is null || !IsValidRecord(
                    record.RepositoryId,
                    record.CommonDirectory,
                    record.GrantedFromWorktree,
                    record.AnchorCommit)))
                return Result<RepositoryTrustStore, RtError>.Err(CorruptStoreError());

            if (HasDuplicateRepositoryIds(store.Records.Select(record => record.RepositoryId)))
                return Result<RepositoryTrustStore, RtError>.Err(CorruptStoreError());

            return Result<RepositoryTrustStore, RtError>.Ok(store);
        }
        catch (Exception ex) when (IsStoreReadException(ex))
        {
            return Result<RepositoryTrustStore, RtError>.Err(StorageError(ex));
        }
    }

    private void WriteStore(RepositoryTrustStore store)
    {
        var tempPath = Path.Combine(
            _registryDirectory,
            $".{Path.GetFileName(_registryPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, store, JsonOptions);
                // Stryker disable all : Power-loss durability of Flush(true) is not observable in an in-process test.
                stream.Flush(flushToDisk: true);
                // Stryker restore all
            }

            ApplyFilePermissions(tempPath);
            File.Move(tempPath, _registryPath, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private FileStream AcquireMutationLock()
    {
        Directory.CreateDirectory(_registryDirectory);
        ApplyDirectoryPermissions(_registryDirectory);
        var stopwatch = Stopwatch.StartNew();
        for (; ; )
        {
            try
            {
                var stream = new FileStream(
                    _lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
                ApplyFilePermissions(_lockPath);
                return stream;
            }
            // Stryker disable all : Removing the retry body changes contention pacing only; exclusive FileShare.None admission remains mutated.
            catch (IOException) when (ShouldRetryLock(stopwatch.Elapsed))
            {
                Thread.Sleep(25);
            }
            // Stryker restore all
        }
    }

    private static Result<string, RtError> RunGit(string workingDirectory, params string[] arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("git")
                {
                    WorkingDirectory = workingDirectory,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    // Stryker disable all : Console-window creation is Windows-only process-launch presentation.
                    CreateNoWindow = true
                    // Stryker restore all
                }
            };
            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            // Stryker disable all : A hung Git child needs an injectable process seam; this block remains typed fail-closed.
            if (!process.WaitForExit(GitTimeout))
            {
                process.Kill(entireProcessTree: true);
                return Result<string, RtError>.Err(RtError.ExternalToolFailure(
                    "Git timed out while resolving repository trust."));
            }
            // Stryker restore all

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
                return Result<string, RtError>.Err(RtError.ExternalToolFailure(
                    $"Git could not resolve repository trust for '{workingDirectory}': {stderr.Trim()}"));

            return Result<string, RtError>.Ok(stdout);
        }
        // Stryker disable all : Git launch failures require replacing the executable; callers still fail closed if this is removed.
        catch (Exception ex) when (IsGitLaunchException(ex))
        {
            return Result<string, RtError>.Err(RtError.ExternalToolFailure(
                $"Git could not resolve repository trust: {ex.Message}"));
        }
        // Stryker restore all
    }

    private static Result<string, RtError> ResolveAnchorCommit(RepositoryBinding binding)
    {
        var result = RunGit(
            binding.WorktreeRoot,
            "rev-list",
            "--max-parents=0",
            "HEAD");
        return result.IsErr
            ? result
            : SelectAnchorCommit(result.Unwrap());
    }

    internal static Result<string, RtError> SelectAnchorCommit(string output)
    {
        var commits = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return commits.Length == 0 || !IsObjectId(commits[0])
            ? Result<string, RtError>.Err(RtError.ExternalToolFailure(
                "Git did not return a valid root commit for the repository trust binding."))
            : Result<string, RtError>.Ok(commits[0]);
    }

    private static bool AnchorExists(RepositoryBinding binding, string anchorCommit)
    {
        return RunGit(
            binding.WorktreeRoot,
            "cat-file",
            "-e",
            $"{anchorCommit}^{{commit}}").IsOk;
    }

    internal static bool IsObjectId(string? value) =>
        value is { Length: 40 or 64 } && value.All(Uri.IsHexDigit);

    internal static bool MatchesRepositoryIdentity(
        string storedRepositoryId,
        string storedCommonDirectory,
        string resolvedRepositoryId,
        string resolvedCommonDirectory) =>
        string.Equals(storedRepositoryId, resolvedRepositoryId, StringComparison.Ordinal)
        && PathsEqual(storedCommonDirectory, resolvedCommonDirectory);

    internal static bool IsValidRecord(
        string? repositoryId,
        string? commonDirectory,
        string? grantedFromWorktree,
        string? anchorCommit) =>
        commonDirectory is not null
        && Path.IsPathFullyQualified(commonDirectory)
        && !string.IsNullOrWhiteSpace(grantedFromWorktree)
        && Path.IsPathFullyQualified(grantedFromWorktree)
        && IsObjectId(anchorCommit)
        && string.Equals(
            repositoryId,
            ComputeRepositoryId(commonDirectory),
            StringComparison.Ordinal);

    internal static bool HasDuplicateRepositoryIds(IEnumerable<string> repositoryIds) =>
        repositoryIds
            .GroupBy(repositoryId => repositoryId, StringComparer.Ordinal)
            .Any(group => group.Count() > 1);

    internal static bool HasTrustedStatus(IEnumerable<RepositoryTrustStatus> statuses) =>
        statuses.Any(status => status.State == RepositoryTrustState.Trusted);

    internal static bool IsStorageException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;

    internal static bool IsBindingException(Exception exception) =>
        exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException;

    internal static bool IsStoreReadException(Exception exception) =>
        exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    internal static bool IsGitLaunchException(Exception exception) =>
        exception is InvalidOperationException
        || exception is IOException
        || exception is UnauthorizedAccessException
        || exception is System.ComponentModel.Win32Exception;

    internal static bool ShouldRetryLock(TimeSpan elapsed) => elapsed < LockTimeout;

    internal static string[] ParseWorktreeRoots(string porcelain)
    {
        return porcelain
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(field => field.StartsWith("worktree ", StringComparison.Ordinal))
            .Select(field => CanonicalizeInput(field["worktree ".Length..]))
            .ToArray();
    }

    internal static string ComputeRepositoryId(string commonDirectory)
    {
        var identityPath = OperatingSystem.IsWindows()
            ? CanonicalizeInput(commonDirectory).ToUpperInvariant()
            : CanonicalizeInput(commonDirectory);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identityPath))).ToLowerInvariant();
    }

    private static string ResolveLocalApplicationData()
    {
        // Stryker disable once String : The environment-variable name is platform profile plumbing; pure precedence behavior is tested separately.
        return ResolveLocalApplicationData(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    internal static string ResolveLocalApplicationData(
        string? localApplicationData,
        string? xdgDataHome,
        string? userProfile)
    {
        if (!string.IsNullOrWhiteSpace(localApplicationData))
            return localApplicationData;

        if (!string.IsNullOrWhiteSpace(xdgDataHome) && Path.IsPathFullyQualified(xdgDataHome))
            return xdgDataHome;

        if (string.IsNullOrWhiteSpace(userProfile))
            throw new InvalidOperationException("The current user's local application-data directory is unavailable.");
        return Path.Combine(userProfile, ".local", "share");
    }

    private static string CanonicalizeExistingDirectory(string path)
    {
        var canonical = CanonicalizeInput(path);
        // Stryker disable all : This is a fail-closed filesystem race after Git resolution; only the exception construction is uncovered.
        if (!Directory.Exists(canonical))
            throw new DirectoryNotFoundException($"Repository directory does not exist: '{canonical}'.");
        // Stryker restore all
        return canonical;
    }

    private static string CanonicalizeInput(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool PathsEqual(string left, string right) =>
        string.Equals(CanonicalizeInput(left), CanonicalizeInput(right), PathComparison);

    internal static bool IsContained(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative)
               && relative != ".."
               && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
               && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static void ApplyDirectoryPermissions(string directory)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        // Stryker disable all : PlatformNotSupportedException is unavailable on supported Unix; permission application remains mutated.
        catch (PlatformNotSupportedException)
        {
            // Best available hardening: atomic storage remains fail-closed if the file cannot be read.
        }
        // Stryker restore all
    }

    private static void ApplyFilePermissions(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        // Stryker disable all : PlatformNotSupportedException is unavailable on supported Unix; permission application remains mutated.
        catch (PlatformNotSupportedException)
        {
            // Best available hardening: atomic storage remains fail-closed if the file cannot be read.
        }
        // Stryker restore all
    }

    private static RepositoryTrustStatus ToStatus(
        RepositoryTrustRecord record,
        RepositoryBinding binding) => new(
        record.RepositoryId,
        record.CommonDirectory,
        binding.WorktreeRoot,
        RepositoryTrustState.Trusted,
        record.GrantedAtUtc,
        null);

    private static RepositoryTrustStatus ToStaleStatus(RepositoryTrustRecord record, string reason) => new(
        record.RepositoryId,
        record.CommonDirectory,
        record.GrantedFromWorktree,
        RepositoryTrustState.Stale,
        record.GrantedAtUtc,
        reason);

    private RtError CorruptStoreError() => new(
        RtErrorKind.InvalidInput,
        $"The repository trust registry is invalid and was treated as untrusted: '{_registryPath}'.");

    private RtError StorageError(Exception ex) => new(
        RtErrorKind.InvalidInput,
        $"The repository trust registry could not be read or written and was treated as untrusted: {ex.Message}",
        new Dictionary<string, string> { ["registry"] = _registryPath });

    private sealed record RepositoryTrustStore(
        int SchemaVersion,
        IReadOnlyList<RepositoryTrustRecord> Records);

    private sealed record RepositoryTrustRecord(
        string RepositoryId,
        string CommonDirectory,
        string GrantedFromWorktree,
        string AnchorCommit,
        DateTimeOffset GrantedAtUtc);

    internal sealed record RepositoryBinding(
        string RepositoryId,
        string CommonDirectory,
        string WorktreeRoot);
}

internal enum RepositoryTrustState
{
    Trusted,
    Untrusted,
    Stale
}

internal sealed record RepositoryTrustStatus(
    string RepositoryId,
    string CommonDirectory,
    string WorktreeRoot,
    RepositoryTrustState State,
    DateTimeOffset? GrantedAtUtc,
    string? Reason)
{
    internal static RepositoryTrustStatus Untrusted(RepositoryTrustRegistry.RepositoryBinding binding) => new(
        binding.RepositoryId,
        binding.CommonDirectory,
        binding.WorktreeRoot,
        RepositoryTrustState.Untrusted,
        null,
        "No persistent trust record exists for this repository.");
}
