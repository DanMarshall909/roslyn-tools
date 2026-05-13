using System.Text.Json;
using System.Text.Json.Serialization;

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

    internal static void Append(FeedbackEntry entry)
    {
        Lock.Wait();
        try
        {
            AppendCore(entry);
        }
        finally
        {
            Lock.Release();
        }
    }

    internal static async Task AppendExceptionAsync(string cmdLine, Exception ex)
    {
        try
        {
            await Lock.WaitAsync();
            try
            {
                var existing = ReadExisting();
                var entry = new FeedbackEntry
                {
                    Id = NextId(existing),
                    Type = "exception",
                    Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                    CommandLine = cmdLine,
                    Description = ex.Message,
                    ActualResult = ex.GetType().Name + ": " + ex.Message,
                    StackTrace = ex.ToString()
                };
                existing.Add(entry);
                WriteAll(existing);
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

    private static void AppendCore(FeedbackEntry entry)
    {
        var all = ReadExisting();
        all.Add(entry);
        WriteAll(all);
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
