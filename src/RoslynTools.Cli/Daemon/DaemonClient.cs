using System.IO.Pipes;
using System.Text.Json;
using RoslynTools.Core;

namespace RoslynTools.Daemon;

/// <summary>
/// A client for interacting with the daemon.
/// </summary>
internal sealed class DaemonClient
{
    internal Task<int?> TryStopDirectAsync(CancellationToken cancellationToken = default) =>
        SendStopOverPipeAsync(DaemonServer.PipeName, cancellationToken);

    // Connects without consulting the state file so an orphan daemon can be stopped.
    internal static async Task<bool> IsPipeReachableAsync(CancellationToken cancellationToken = default)
    {
        await using var pipe =
            new NamedPipeClientStream(".", DaemonServer.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(1));
            await pipe.ConnectAsync(deadline.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal async Task<int?> SendStopOverPipeAsync(
        string pipeName,
        CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));

        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await using var pipeOwn = pipe;
        // A pending NamedPipeClientStream read may ignore cancellation on Windows.
        await using var registration = deadline.Token.Register(() =>
        {
            try
            {
                pipe.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (IOException)
            {
            }
        });

        try
        {
            using var connectDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            connectDeadline.CancelAfter(TimeSpan.FromSeconds(1));
            await pipe.ConnectAsync(connectDeadline.Token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException
                                          or TimeoutException or ObjectDisposedException)
        {
            return null;
        }

        try
        {
            // Retain the old envelope shape for stop requests to older orphan daemons.
            var request = new DaemonRequest(
                Guid.NewGuid().ToString("N")[..8],
                ["stop"],
                new GitState("unknown", false, null));
            var writer = new StreamWriter(pipe, DaemonEncoding.NoBom, 1024, true) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, DaemonProtocol.JsonOpts).AsMemory(),
                deadline.Token);

            using var reader = new StreamReader(pipe, DaemonEncoding.NoBom, false, 1024, true);
            while (!deadline.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(deadline.Token);
                if (line is null)
                    break;

                var response = JsonSerializer.Deserialize<DaemonResponse>(line, DaemonProtocol.JsonOpts);
                if (response is null)
                    continue;

                switch (response.Type)
                {
                    case "stdout": RtConsole.Write(RtOutputContext.Result, response.Data ?? ""); break;
                    case "stderr": RtConsole.Write(RtOutputContext.Diagnostic, response.Data ?? ""); break;
                    case "exit": return response.Code ?? 0;
                }
            }

            return null;
        }
        catch (Exception) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            RtLog.Warning("[rt] Daemon did not respond within 10s.");
            return null;
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException
                                          or ObjectDisposedException)
        {
            return null;
        }
    }
}
