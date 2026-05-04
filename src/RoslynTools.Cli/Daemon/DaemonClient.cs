using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RoslynTools.Core;

namespace RoslynTools.Daemon;

internal static class DaemonClient
{
    /// <summary>
    /// Sends <paramref name="args"/> to the running daemon and streams its stdout/stderr.
    /// Returns the exit code, or null if no healthy daemon is available (caller falls back to in-process).
    /// </summary>
    internal static async Task<int?> TrySendAsync(string[] args, CancellationToken ct = default)
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
            // Binary was rebuilt — let the in-process path run, caller can restart the daemon.
            RtLog.Warning("[rt] Daemon version mismatch — falling back to in-process. Run `rt daemon start` to refresh.");
            return null;
        }

        using var pipe = new NamedPipeClientStream(".", state.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            await pipe.ConnectAsync(cts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            RtLog.Warning("[rt] Could not connect to daemon — falling back to in-process.");
            DaemonState.Remove();
            return null;
        }

        // Connected — IO errors past this point don't mean the daemon is dead.
        try
        {
            var request = new DaemonRequest(
                RequestId: Guid.NewGuid().ToString("N")[..8],
                Args: args,
                GitState: GetGitState(),
                WorkingDirectory: Directory.GetCurrentDirectory());

            var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, DaemonProtocol.JsonOpts).AsMemory(), ct);

            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
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
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            // Server disconnected after sending exit — daemon is still alive, don't remove state.
            return null;
        }
    }

    private static GitState GetGitState()
    {
        try
        {
            var head = RunGit("rev-parse HEAD").Trim();
            var status = RunGit("status --porcelain");
            var dirty = !string.IsNullOrWhiteSpace(status);
            var indexMtime = dirty ? GetIndexMtime() : (long?)null;
            return new GitState(head, dirty, indexMtime);
        }
        catch
        {
            return new GitState("unknown", false, null);
        }
    }

    private static long? GetIndexMtime()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            var index = Path.Combine(dir, ".git", "index");
            if (File.Exists(index))
                return new FileInfo(index).LastWriteTimeUtc.ToFileTimeUtc();
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    private static string RunGit(string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return output;
    }
}
