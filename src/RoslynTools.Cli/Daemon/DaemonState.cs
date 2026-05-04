using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace RoslynTools.Daemon;

internal sealed record DaemonStateFile(
    int Pid,
    string PipeName,
    string? Solution,
    string? CachedHead,
    bool CachedDirty,
    long? CachedIndexMtime,
    DateTimeOffset StartedAt,
    string RtVersion);

internal static class DaemonState
{
    internal static readonly string StateDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rt");

    private static readonly string StateFile = Path.Combine(StateDir, "daemon.json");

    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static DaemonStateFile? TryRead()
    {
        if (!File.Exists(StateFile)) return null;
        try
        {
            var json = File.ReadAllText(StateFile);
            return JsonSerializer.Deserialize<DaemonStateFile>(json, DaemonProtocol.JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    internal static void Write(DaemonStateFile state)
    {
        Directory.CreateDirectory(StateDir);
        var tmp = StateFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, WriteOpts));
        File.Move(tmp, StateFile, overwrite: true);
    }

    internal static void Remove()
    {
        try { File.Delete(StateFile); } catch { /* best effort */ }
    }

    internal static bool IsAlive(DaemonStateFile state)
    {
        try
        {
            using var proc = Process.GetProcessById(state.Pid);
            return !proc.HasExited;
        }
        catch
        {
            return false;
        }
    }

    // Uses exe mtime as version so any rebuild causes a daemon restart.
    internal static string CurrentVersion()
    {
        var exe = Environment.ProcessPath;
        if (exe is null || !File.Exists(exe)) return "unknown";
        return File.GetLastWriteTimeUtc(exe).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
    }
}
