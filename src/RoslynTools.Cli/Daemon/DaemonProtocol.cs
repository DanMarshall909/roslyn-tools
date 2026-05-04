using System.Text.Json;

namespace RoslynTools.Daemon;

internal sealed record DaemonRequest(
    string RequestId,
    string[] Args,
    GitState GitState,
    string? WorkingDirectory = null,
    string? Stdin = null);

internal sealed record GitState(
    string Head,
    bool Dirty,
    long? IndexMtime);

// Type: "stdout" | "stderr" | "exit"
internal sealed record DaemonResponse(
    string RequestId,
    string Type,
    string? Data = null,
    int? Code = null);

internal static class DaemonProtocol
{
    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}
