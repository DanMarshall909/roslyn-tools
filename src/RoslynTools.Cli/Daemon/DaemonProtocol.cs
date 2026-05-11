using System.Text.Json;

namespace RoslynTools.Daemon;

/// <summary>
/// A request object for daemon communication.
/// </summary>
internal sealed record DaemonRequest(
    string RequestId,
    string[] Args,
    GitState GitState,
    string? WorkingDirectory = null,
    string? Stdin = null);

/// <summary>
/// A representation of the git state for the daemon.
/// </summary>
internal sealed record GitState(
    string Head,
    bool Dirty,
    long? IndexMtime);

// Type: "stdout" | "stderr" | "exit"
/// <summary>
/// A response object for daemon communication.
/// </summary>
internal sealed record DaemonResponse(
    string RequestId,
    string Type,
    string? Data = null,
    int? Code = null);

/// <summary>
/// The protocol used for daemon communication.
/// </summary>
internal static class DaemonProtocol
{
    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}
