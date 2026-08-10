using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Feedback;

internal static class FeedbackStore
{
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

    internal static List<FeedbackEntry> LoadAll()
    {
        var path = ResolveStorePath();
        if (!File.Exists(path)) return [];

        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<FeedbackFile>(json, ReadOpts);
            return doc?.FeedbackItems ?? [];
        }
        catch
        {
            return [];
        }
    }

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
            using var storeLock = AcquireStoreLock();
            return AppendCore(entry);
        }
        finally
        {
            Lock.Release();
        }
    }

    internal static FeedbackEntry AddUserFeedback(
        string title,
        FeedbackCategory category,
        string description,
        string? sampleCode,
        string? expectedResult,
        string? commandLine,
        string? command)
    {
        var entry = new FeedbackEntry
        {
            Type = "user-feedback",
            Timestamp = DateTimeOffset.UtcNow.ToString("O"),
            Title = title,
            Category = category,
            Command = command,
            CommandLine = commandLine,
            Description = description,
            SampleCode = sampleCode,
            ExpectedResult = expectedResult,
            // Captured for every report, not just exceptions, so entries are
            // consistently structured whether or not this one is ever promoted
            // to a GitHub issue.
            RtVersion = SafeGet(GetRtVersion),
            DotnetVersion = SafeGet(() => Environment.Version.ToString()),
            OsPlatform = SafeGet(() => RuntimeInformation.OSDescription)
        };

        Append(entry);
        return entry;
    }

    internal static FeedbackEntry? TryGetById(string id) =>
        LoadAll().SingleOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    ///     Records that <paramref name="id" /> was filed (or linked) as GitHub issue
    ///     <paramref name="issueNumber" /> by <c>rt feedback report</c>. Reuses the same
    ///     process/status shape as <see cref="MarkProcessed" />.
    /// </summary>
    internal static FeedbackEntry MarkReported(string id, int issueNumber, string issueUrl)
    {
        Lock.Wait();
        try
        {
            using var storeLock = AcquireStoreLock();
            var all = ReadExisting();
            var entry = all.SingleOrDefault(candidate =>
                string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                throw new ArgumentException($"Feedback item '{id}' was not found.", nameof(id));

            entry.GitHubIssueNumber = issueNumber;
            entry.GitHubIssueUrl = issueUrl;
            entry.Status = "processed";
            entry.ProcessedAt = DateTimeOffset.UtcNow.ToString("O");
            entry.Resolution = $"Reported as GitHub issue #{issueNumber}: {issueUrl}";
            WriteAll(all);
            return entry;
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
            using var storeLock = AcquireStoreLock();
            var all = ReadExisting();
            var entry = all.SingleOrDefault(candidate =>
                string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                throw new ArgumentException($"Feedback item '{id}' was not found.", nameof(id));

            entry.Status = "processed";
            entry.ProcessedAt = DateTimeOffset.UtcNow.ToString("O");
            entry.Resolution = resolution;
            WriteAll(all);
            return entry;
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>
    ///     Returns the captured entry (so callers can point the user at
    ///     <c>rt feedback report --id &lt;id&gt;</c>), or <c>null</c> if capture itself failed —
    ///     exception capture must never mask the original exception.
    /// </summary>
    internal static async Task<FeedbackEntry?> AppendExceptionAsync(
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
                using var storeLock = AcquireStoreLock();
                var existing = ReadExisting();
                var entry = new FeedbackEntry
                {
                    Id = NextId(existing),
                    Type = "exception",
                    Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                    // Best-effort structure so a captured exception is reportable as-is via
                    // `rt feedback report` without forcing the human to re-classify it first.
                    Title = ex.Message.TruncateOneLine(100),
                    Category = FeedbackCategory.Bug,
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
                WriteAll(existing);
                return entry;
            }
            finally
            {
                Lock.Release();
            }
        }
        catch
        {
            // Exception capture must never mask the original exception
            return null;
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

    private static string AppendCore(FeedbackEntry entry)
    {
        var all = ReadExisting();
        if (string.IsNullOrWhiteSpace(entry.Id)
            || all.Any(existing => string.Equals(existing.Id, entry.Id, StringComparison.Ordinal)))
            entry.Id = NextId(all);

        all.Add(entry);
        WriteAll(all);
        return entry.Id;
    }

    private static FileStream AcquireStoreLock()
    {
        var path = ResolveStorePath();
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var lockPath = path + ".lock";
        for (var attempt = 0; attempt < 100; attempt++)
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.DeleteOnClose);
            }
            catch (IOException) when (attempt < 99)
            {
                Thread.Sleep(20);
            }

        return new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            1,
            FileOptions.DeleteOnClose);
    }

    private static List<FeedbackEntry> ReadExisting()
    {
        var path = ResolveStorePath();
        if (!File.Exists(path)) return [];

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<FeedbackFile>(json, ReadOpts)?.FeedbackItems ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void WriteAll(List<FeedbackEntry> entries)
    {
        var path = ResolveStorePath();
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var file = new FeedbackFile { FeedbackItems = entries };
        var json = JsonSerializer.Serialize(file, WriteOpts);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    private sealed class FeedbackFile
    {
        public List<FeedbackEntry> FeedbackItems { get; set; } = [];
    }
}
