using System.Text.Json;
using RoslynTools.Daemon.Rpc;

namespace RoslynTools.Mcp.Daemon;

/// <summary>Recovers a missing daemon once, then leaves adapter fallback policy unchanged.</summary>
internal sealed class DaemonRpcRecovery(
    IDaemonRpcClient daemonRpc,
    IMcpDaemonLauncher daemonLauncher)
{
    internal async Task<DaemonRpcResponse?> TryExecuteAsync(
        string commandName,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var response = await daemonRpc.TryExecuteAsync(commandName, arguments, cancellationToken);
        if (response is not null)
            return response;

        if (!await daemonLauncher.EnsureStartedAsync(cancellationToken))
            return null;

        return await daemonRpc.TryExecuteAsync(commandName, arguments, cancellationToken);
    }
}
