using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoslynTools.Feedback;

internal static class FeedbackStore
{
    private const int WindowsSharingViolation = 32;
    private const int WindowsLockViolation = 33;
    private const int WindowsAccessDenied = 5;
    private const int LinuxWouldBlock = 11;
    private const int BsdWouldBlock = 35;
    private const int StoreWriteMaxAttempts = 50;

    private static readonly TimeSpan StoreLockRetryDelay = TimeSpan.FromMilliseconds(20);
    private static readonly SemaphoreSlim Lock = new(1, 1);

    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static string ResolveStorePath()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, "ralph")))
                return Path.Combine(dir, "ralph", "feedback.json");
            dir = Directory.GetParent(dir)?.FullName;
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rt");
        Directory.CreateDirectory(fallback);
        return Path.Combine(fallback, "feedback.json");
    }

    internal static List<FeedbackEntry> LoadAll() => ReadExisting(ResolveStorePath());

    internal static string NextId(List<FeedbackEntry> existing)
    {
        var max = 0;
        foreach (var e in existing)
        {
            if (e.Id.StartsWith("FB-", StringComparison.Ordinal)
                && int.TryParse(e.Id[3..], out var n) && n > max)
                max = n;
        }

        return $"FB-{max + 1:D3}";
    }

    internal static string Append(FeedbackEntry entry)
    {
        Lock.Wait();
        try
        {
            var storePath = ResolveStorePath();
            using var storeLock = AcquireStoreLock(storePath);
            return AppendCore(entry, storePath);
        }
        finally
        {
            Lock.Release();
        }
    }

    internal static FeedbackEntry AddUserFeedback(
        string description,
        string? sampleCode,
        string? expectedResult,
        string? commandLine)
    {
        var entry = new FeedbackEntry
        {
            Type = "user-feedback",
            Timestamp = DateTimeOffset.UtcNow.ToString("O"),
            CommandLine = commandLine,
            Description = description,
            SampleCode = sampleCode,
            ExpectedResult = expectedResult
        };

        Append(entry);
        return entry;
    }

    internal static FeedbackEntry? TryGetById(string id) =>
        LoadAll().SingleOrDefault(entry =>
            string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    ///     Atomically creates or links an issue for <paramref name="id" />. The final
    ///     reported-state check and issue creation run under the cross-process store
    ///     lock so concurrent reporters cannot create duplicate public issues.
    /// </summary>
    internal static async Task<(FeedbackEntry Entry, bool AlreadyReported)> ReportOnceAsync(
        string id,
        Func<FeedbackEntry, CancellationToken, Task<(int Number, string Url)>> createIssue,
        CancellationToken cancellationToken = default)
    {
        await Lock.WaitAsync(cancellationToken);
        try
        {
            var storePath = ResolveStorePath();
            using var storeLock = await AcquireStoreLockAsync(storePath, cancellationToken);
            var all = ReadExisting(storePath);
            var entry = all.SingleOrDefault(candidate =>
                string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                throw new ArgumentException($"Feedback item '{id}' was not found.", nameof(id));

            if (entry.GitHubIssueNumber is not null)
                return (entry, true);

            var (issueNumber, issueUrl) = await createIssue(entry, cancellationToken);
            entry.GitHubIssueNumber = issueNumber;
            entry.GitHubIssueUrl = issueUrl;
            entry.Status = "processed";
            entry.ProcessedAt = DateTimeOffset.UtcNow.ToString("O");
            entry.Resolution = $"Reported as GitHub issue #{issueNumber}: {issueUrl}";
            WriteAll(all, storePath);
            return (entry, false);
        }
        finally
        {
            Lock.Release();
        }
    }

    internal static List<FeedbackEntry> ListByStatus(string status)
    {
        var all = LoadAll();
        return status switch
        {
            "all" => all,
            "processed" => all.Where(e =>
                string.Equals(e.Status, "processed", StringComparison.OrdinalIgnoreCase)).ToList(),
            _ => all.Where(e =>
                string.Equals(e.Status, "pending", StringComparison.OrdinalIgnoreCase)).ToList()
        };
    }

    internal static FeedbackEntry MarkProcessed(string id, string resolution)
    {
        Lock.Wait();
        try
        {
            var storePath = ResolveStorePath();
            using var storeLock = AcquireStoreLock(storePath);
            var all = ReadExisting(storePath);
            var entry = all.SingleOrDefault(candidate =>
                string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                throw new ArgumentException($"Feedback item '{id}' was not found.", nameof(id));

            entry.Status = "processed";
            entry.ProcessedAt = DateTimeOffset.UtcNow.ToString("O");
            entry.Resolution = resolution;
            WriteAll(all, storePath);
            return entry;
        }
        finally
        {
            Lock.Release();
        }
    }

    internal static async Task AppendExceptionAsync(
        string cmdLine,
        Exception ex,
        string? solutionPath = null,
        Dictionary<string, string?>? parsedOptions = null)
    {
        try
        {
            await Lock.WaitAsync();
            try
            {
                var storePath = ResolveStorePath();
                using var storeLock = await AcquireStoreLockAsync(
                    storePath,
                    CancellationToken.None);
                var existing = ReadExisting(storePath);
                var entry = new FeedbackEntry
                {
                    Id = NextId(existing),
                    Type = "exception",
                    Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                    CommandLine = cmdLine,
                    Description = ex.Message,
                    ActualResult = ex.GetType().Name + ": " + ex.Message,
                    StackTrace = ex.ToString(),
                    Cwd = SafeGet(Directory.GetCurrentDirectory),
                    RtVersion = SafeGet(GetRtVersion),
                    DotnetVersion = SafeGet(() => Environment.Version.ToString()),
                    OsPlatform = SafeGet(() => RuntimeInformation.OSDescription),
                    SolutionPath = solutionPath,
                    InnerExceptions = FlattenInner(ex),
                    ParsedOptions = parsedOptions
                };
                existing.Add(entry);
                WriteAll(existing, storePath);
            }
            finally
            {
                Lock.Release();
            }
        }
        catch
        {
            // Exception capture must never mask the original exception
        }
    }

    private static string? SafeGet(Func<string?> get)
    {
        try { return get(); }
        catch { return null; }
    }

    private static string? GetRtVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? asm.GetName().Version?.ToString();
    }

    private static List<string>? FlattenInner(Exception ex)
    {
        if (ex.InnerException is null) return null;
        var chain = new List<string>();
        var inner = ex.InnerException;
        while (inner is not null)
        {
            chain.Add(inner.GetType().Name + ": " + inner.Message);
            inner = inner.InnerException;
        }
        return chain;
    }

    private static string AppendCore(FeedbackEntry entry, string storePath)
    {
        var all = ReadExisting(storePath);
        if (string.IsNullOrWhiteSpace(entry.Id)
            || all.Any(existing => string.Equals(existing.Id, entry.Id, StringComparison.Ordinal)))
            entry.Id = NextId(all);

        all.Add(entry);
        WriteAll(all, storePath);
        return entry.Id;
    }

    private static FileStream AcquireStoreLock(string storePath)
    {
        var lockPath = ResolveStoreLockPath(storePath);
        while (true)
            try
            {
                return OpenStoreLock(lockPath);
            }
            catch (IOException ex) when (IsStoreLockContention(ex))
            {
                Thread.Sleep(StoreLockRetryDelay);
            }
    }

    private static async Task<FileStream> AcquireStoreLockAsync(
        string storePath,
        CancellationToken cancellationToken)
    {
        var lockPath = ResolveStoreLockPath(storePath);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return OpenStoreLock(lockPath);
            }
            catch (IOException ex) when (IsStoreLockContention(ex))
            {
                await Task.Delay(StoreLockRetryDelay, cancellationToken);
            }
        }
    }

    internal static bool IsStoreLockContention(IOException exception)
    {
        var errorCode = exception.HResult & 0xFFFF;
        return OperatingSystem.IsWindows()
            ? errorCode is WindowsSharingViolation or WindowsLockViolation
            : errorCode is LinuxWouldBlock or BsdWouldBlock;
    }

    private static string ResolveStoreLockPath(string storePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
        return storePath + ".lock";
    }

    private static FileStream OpenStoreLock(string lockPath) =>
        new(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            1,
            FileOptions.None);

    private static List<FeedbackEntry> ReadExisting(string path)
    {
        if (!File.Exists(path)) return [];

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<FeedbackFile>(stream, ReadOpts)?.FeedbackItems ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void WriteAll(List<FeedbackEntry> entries, string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var file = new FeedbackFile { FeedbackItems = entries };
        var json = JsonSerializer.Serialize(file, WriteOpts);

        var tmp = Path.Combine(
            dir,
            $".{Path.GetFileName(path)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tmp, json);
            ReplaceStoreFile(tmp, path);
        }
        finally
        {
            TryDeleteTemporaryFile(tmp);
        }
    }

    private static void ReplaceStoreFile(string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when (ShouldRetryStoreWrite(ex, attempt))
            {
                Thread.Sleep(StoreLockRetryDelay);
            }
        }
    }

    private static bool ShouldRetryStoreWrite(Exception exception, int attempt)
    {
        if (!OperatingSystem.IsWindows() || attempt >= StoreWriteMaxAttempts)
            return false;

        var errorCode = exception.HResult & 0xFFFF;
        return (exception is IOException or UnauthorizedAccessException)
               && errorCode is (WindowsAccessDenied
                   or WindowsSharingViolation
                   or WindowsLockViolation);
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A failed write must not be hidden by best-effort temporary-file cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // A failed write must not be hidden by best-effort temporary-file cleanup.
        }
    }

    private sealed class FeedbackFile
    {
        public List<FeedbackEntry> FeedbackItems { get; set; } = [];
    }
}
