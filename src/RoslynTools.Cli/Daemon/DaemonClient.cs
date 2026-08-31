using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RoslynTools.Core;

namespace RoslynTools.Daemon;

/// <summary>
/// An encoding type used by the daemon.
/// </summary>
internal static class DaemonEncoding
{
    // UTF-8 without BOM — both client and server use this. Encoding.UTF8 emits a 3-byte BOM
    // on first flush, which the JSON deserializer on the other end rejects with
    // "'0xEF' is an invalid start of a value".
    internal static readonly UTF8Encoding NoBom = new(false, true);

    // Display string for the pipe — purely cosmetic, used by `rt maintainer daemon status` and the
    // daemon's startup log line. .NET's NamedPipeServerStream on Linux/macOS is implemented
    // over a Unix domain socket at /tmp/CoreFxPipe_<name>; on Windows it's \\.\pipe\<name>.
    /// <summary>
    /// A method to display the pipe path for the daemon encoding.
    /// </summary>
    internal static string DisplayPipePath(string pipeName)
    {
        return OperatingSystem.IsWindows()
            ? $@"\\.\pipe\{pipeName}"
            : $"/tmp/CoreFxPipe_{pipeName}";
    }
}

/// <summary>
/// A client for interacting with the daemon.
/// </summary>
internal sealed class DaemonClient(GitService git)
{
    // Ceiling on piped stdin forwarded over the pipe envelope. The realistic payload is a
    // find-refs/describe JSON blob (tens of KB); this cap only exists to reject a pathological
    // multi-megabyte pipe rather than buffer it whole into one JSON line on the server.
    private const int MaxForwardedStdinChars = 8 * 1024 * 1024;

    /// <summary>
    /// A method to send asynchronous data to the daemon.
    /// </summary>
    internal async Task<int?> TrySendAsync(string[] args, CancellationToken ct = default)
    {
        var state = DaemonState.TryRead();
        if (state is null) return null;

        if (!DaemonState.IsAlive(state))
        {
            DaemonState.Remove();
            return null;
        }

        if (state.RtVersion != DaemonState.CurrentVersion())
        {
            RtLog.Warning(
                "[rt] Daemon version mismatch — falling back to in-process. Run `rt maintainer daemon stop`, then `rt maintainer daemon start` to refresh.");
            return null;
        }

        return await SendOverPipeAsync(state.PipeName, args, ct);
    }

    // Connects to the well-known pipe without consulting the state file. Used by lifecycle
    // commands to talk to (or stop) an orphan daemon whose state file was lost.
    /// <summary>
    /// A method to send direct asynchronous data to the daemon.
    /// </summary>
    internal Task<int?> TrySendDirectAsync(string[] args, CancellationToken ct = default)
    {
        return SendOverPipeAsync(DaemonServer.PipeName, args, ct);
    }

    /// <summary>
    /// A method to check if the pipe is reachable.
    /// </summary>
    internal static async Task<bool> IsPipeReachableAsync(CancellationToken ct = default)
    {
        await using var pipe =
            new NamedPipeClientStream(".", DaemonServer.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(1));
            await pipe.ConnectAsync(cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<int?> SendOverPipeAsync(string pipeName, string[] args, CancellationToken ct)
    {
        // Hard ceiling on the entire client request — connect, send, and stream responses.
        // 10 seconds is enough for any healthy daemon round-trip; if it isn't, the daemon is
        // wedged and we fall back to in-process rather than letting the user stare at the cursor.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));

        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        // StreamReader.ReadLineAsync on a NamedPipeClientStream does not reliably honour
        // CancellationToken on Windows — pending reads ignore cancellation. Close the pipe
        // when the deadline fires; the read then throws IOException and unblocks.
        await using var pipeOwn = pipe;
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
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            connectCts.CancelAfter(TimeSpan.FromSeconds(1));
            await pipe.ConnectAsync(connectCts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or TimeoutException
                                       or ObjectDisposedException)
        {
            // Connect failure is transient (daemon mid-init, pipe momentarily busy). Fall back to
            // in-process for this invocation but do NOT remove the state file — that turns a busy
            // daemon into an orphan we can no longer find.
            return null;
        }

        try
        {
            var workDir = Directory.GetCurrentDirectory();

            // Forward piped stdin so chained commands (e.g. `rt inspect find-refs --json |
            // rt refactor rename --new-name X`) work through the daemon, not just in-process.
            // Read only after a successful connect so the connect-failure fallback above does
            // not consume stdin that the in-process retry would need.
            string? stdin = null;
            if (RtConsole.IsInputRedirected)
            {
                var raw = await RtConsole.In.ReadToEndAsync(deadline.Token);
                if (raw.Length > MaxForwardedStdinChars)
                {
                    RtConsole.Log(RtLogLevel.Error,
                        "Piped input is too large to forward to the daemon ({Length} chars > {Max}).",
                        raw.Length, MaxForwardedStdinChars);
                    return 1;
                }

                if (raw.Length > 0)
                    stdin = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            }

            var request = new DaemonRequest(
                Guid.NewGuid().ToString("N")[..8],
                args,
                git.GetState(workDir),
                workDir,
                stdin);

            var writer = new StreamWriter(pipe, DaemonEncoding.NoBom, 1024, true) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, DaemonProtocol.JsonOpts).AsMemory(),
                deadline.Token);

            using var reader = new StreamReader(pipe, DaemonEncoding.NoBom, false, 1024, true);
            while (!deadline.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(deadline.Token);
                if (line is null) break;

                var response = JsonSerializer.Deserialize<DaemonResponse>(line, DaemonProtocol.JsonOpts);
                if (response is null) continue;

                switch (response.Type)
                {
                    case "stdout": RtConsole.Write(RtOutputContext.Result, response.Data ?? ""); break;
                    case "stderr": RtConsole.Write(RtOutputContext.Diagnostic, response.Data ?? ""); break;
                    case "exit": return response.Code ?? 0;
                }
            }

            return 0;
        }
        catch (Exception) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            RtLog.Warning("[rt] Daemon did not respond within 10s — falling back to in-process.");
            return null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            return null;
        }
    }
}
