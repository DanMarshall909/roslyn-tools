using System.CommandLine.Invocation;
using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

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
        var safety = DaemonOwnedCommands.All.FirstOrDefault(command =>
            string.Equals(command.Name, commandName, StringComparison.Ordinal))?.Safety
            ?? CommandSafety.ReadOnly;
        var attempt = await daemon.ExecuteAsync(
            commandName,
            JsonSerializer.SerializeToElement(request),
            safety,
            CancellationToken.None);
        if (attempt.Response is null)
        {
            RtLog.Error("The daemon is unavailable. Start it with `rt daemon start` and retry.");
            context.ExitCode = 1;
            return;
        }

        RenderResponse(attempt.Response, context);
    }

    internal static async Task ExecuteAsync<TRequest>(
        DaemonRpcRecoveryCoordinator recovery,
        ICommandApplicationPort port,
        TRequest request,
        InvocationContext context)
        where TRequest : class
    {
        var cancellationToken = context.GetCancellationToken();
        var result = await recovery.ExecuteAsync(
            port.Definition,
            JsonSerializer.SerializeToElement(request),
            cancellationToken);
        if (result.Response is not null)
        {
            RenderResponse(result.Response, context);
            return;
        }

        if (!result.ShouldFallback)
        {
            RtLog.Error(
                "The daemon accepted this mutation but its result is indeterminate; inspect the target before retrying.");
            context.ExitCode = 1;
            return;
        }

        var local = await port.InvokeAsync(request, cancellationToken);
        if (local.IsError)
        {
            RtLog.Error("Error: {Message}", local.Error?.Message ?? "Command failed.");
            context.ExitCode = 1;
            return;
        }

        var execution = new CommandExecutionResult(local.Value, port.Definition.SuggestedTools);
        RtResultRenderer.Write(RtResult<JsonElement>.Ok(
            DaemonRpcJson.SerializeExecution(execution)));
    }

    private static void RenderResponse(DaemonRpcResponse response, InvocationContext context)
    {
        if (response.Error is not null)
        {
            RtLog.Error("Error: {Message}", response.Error.Message);
            context.ExitCode = 1;
            return;
        }

        RtResultRenderer.Write(RtResult<JsonElement>.Ok(response.Result ?? default));
    }
}
