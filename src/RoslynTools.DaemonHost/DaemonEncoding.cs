using System.Text;

namespace RoslynTools.Daemon;

/// <summary>Shared encoding and display facts for the daemon control pipe.</summary>
internal static class DaemonEncoding
{
    // UTF-8 without BOM: System.Text.Json rejects a BOM at the start of a pipe line.
    internal static readonly UTF8Encoding NoBom = new(false, true);

    internal static string DisplayPipePath(string pipeName) =>
        OperatingSystem.IsWindows()
            ? $@"\\.\pipe\{pipeName}"
            : $"/tmp/CoreFxPipe_{pipeName}";
}
