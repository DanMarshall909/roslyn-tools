using System.CommandLine.Invocation;
using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Daemon.Rpc;

/// <summary>Executes one typed daemon request and maps its terminal response to CLI output.</summary>
internal static class DaemonCommandExecutor
{
    internal static async Task ExecuteAsync<TRequest>(
        DaemonRpcClient daemon,
        string commandName,
        TRequest request,
        InvocationContext context)
    {
        var response = await daemon.TryExecuteAsync(
            commandName,
            JsonSerializer.SerializeToElement(request),
            CancellationToken.None);
        if (response is null)
        {
            RtLog.Error("The daemon is unavailable. Start it with `rt daemon start` and retry.");
            context.ExitCode = 1;
            return;
        }

        if (response.Error is not null)
        {
            RtLog.Error("Error: {Message}", response.Error.Message);
            context.ExitCode = 1;
            return;
        }

        RtResultRenderer.Write(RtResult<JsonElement>.Ok(response.Result ?? default));
    }
}
