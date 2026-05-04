using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RoslynTools.Core;

namespace RoslynTools.Daemon;

internal static class DaemonServer
{
    internal const string PipeName = "rt-daemon";

    internal static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        using var pipe = new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous);

        // Write state file only after pipe is listening — client polls for this file.
        DaemonState.Write(new DaemonStateFile(
            Pid: Environment.ProcessId,
            PipeName: PipeName,
            Solution: null,
            CachedHead: null,
            CachedDirty: false,
            CachedIndexMtime: null,
            StartedAt: DateTimeOffset.UtcNow,
            RtVersion: DaemonState.CurrentVersion()));

        RtLog.Info("[rt-daemon] PID {Pid} listening on \\\\.\\pipe\\{PipeName}", Environment.ProcessId, PipeName);

        var stopping = false;
        while (!stopping && !cancellationToken.IsCancellationRequested)
        {
            await pipe.WaitForConnectionAsync(cancellationToken);
            try
            {
                stopping = await HandleRequestAsync(pipe, cancellationToken);
            }
            finally
            {
                pipe.Disconnect();
            }
        }

        DaemonState.Remove();
        return 0;
    }

    private static async Task<bool> HandleRequestAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        // leaveOpen: true — pipe stream outlives the reader/writer wrappers
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        var line = await reader.ReadLineAsync(ct);
        if (line is null) return false;

        DaemonRequest? request;
        try { request = JsonSerializer.Deserialize<DaemonRequest>(line, DaemonProtocol.JsonOpts); }
        catch { await RespondAsync(writer, new DaemonResponse("?", "exit", Code: 1)); return false; }

        if (request is null) return false;

        if (request.Args is ["ping"])
        {
            await RespondAsync(writer, new DaemonResponse(request.RequestId, "stdout", Data: "pong\n"));
            await RespondAsync(writer, new DaemonResponse(request.RequestId, "exit", Code: 0));
            return false;
        }

        if (request.Args is ["stop"] or ["daemon", "stop"])
        {
            await RespondAsync(writer, new DaemonResponse(request.RequestId, "stdout", Data: "Daemon stopping.\n"));
            await RespondAsync(writer, new DaemonResponse(request.RequestId, "exit", Code: 0));
            return true;
        }

        // Stage 1 will route arbitrary args through App.RunAsync here.
        await RespondAsync(writer, new DaemonResponse(request.RequestId, "stderr",
            Data: "[rt-daemon] command routing not yet implemented (Stage 0)\n"));
        await RespondAsync(writer, new DaemonResponse(request.RequestId, "exit", Code: 1));
        return false;
    }

    private static async Task RespondAsync(StreamWriter writer, DaemonResponse response)
        => await writer.WriteLineAsync(JsonSerializer.Serialize(response, DaemonProtocol.JsonOpts));
}
