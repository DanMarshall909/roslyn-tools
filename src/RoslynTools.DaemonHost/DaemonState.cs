using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace RoslynTools.Daemon;

/// <summary>
/// Class representing the state file for the Roslyn Daemon.
/// </summary>
internal sealed record DaemonStateFile(
    int Pid,
    string PipeName,
    string? Solution,
    string? CachedHead,
    bool CachedDirty,
    long? CachedIndexMtime,
    DateTimeOffset StartedAt,
    string RtVersion);

/// <summary>
/// Class representing the state of the Roslyn Daemon.
/// </summary>
internal static class DaemonState
{
    internal static readonly string StateDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rt");

    private static readonly string StateFile = Path.Combine(StateDir, "daemon.json");
    internal static readonly string LogFile = Path.Combine(StateDir, "daemon.log");

    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Method to attempt reading the Daemon State file.
    /// </summary>
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

    /// <summary>
    /// Method to write the Daemon State file.
    /// </summary>
    internal static void Write(DaemonStateFile state)
    {
        Directory.CreateDirectory(StateDir);
        var tmp = StateFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, WriteOpts));
        File.Move(tmp, StateFile, true);
    }

    /// <summary>
    /// Method to remove the Daemon State.
    /// </summary>
    internal static void Remove()
    {
        try
        {
            File.Delete(StateFile);
        }
        catch
        {
            /* best effort */
        }
    }

    /// <summary>
    /// Method to check if the Daemon State file is alive.
    /// </summary>
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
    /// <summary>
    /// Method to get the current version of the Daemon State.
    /// </summary>
    internal static string CurrentVersion()
    {
        var exe = Environment.ProcessPath;
        if (exe is null || !File.Exists(exe)) return "unknown";
        return File.GetLastWriteTimeUtc(exe).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
    }
}
