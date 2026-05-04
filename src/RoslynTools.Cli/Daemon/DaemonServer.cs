using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RoslynTools;
using RoslynTools.Core;

namespace RoslynTools.Daemon;

internal static class DaemonServer
{
    internal const string PipeName = "rt-daemon";

    internal static async Task<int> RunAsync()
    {
        Directory.CreateDirectory(DaemonState.StateDir);

        using var pipe = new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous);

        // Write state file immediately after pipe creation so `rt daemon start` sees it fast.
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

        // Initialize MSBuild and DI after writing state file — startup signals readiness
        // before the slower MSBuild registration completes. Clients connecting during init
        // block on WaitForConnectionAsync (called below) until init finishes.
        MsBuildBootstrap.Register();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(Bootstrap.RegisterServices)
            .Build();
        var services = host.Services;
        CancellationToken cancellationToken = default;

        var stopping = false;
        while (!stopping && !cancellationToken.IsCancellationRequested)
        {
            await pipe.WaitForConnectionAsync(cancellationToken);
            try
            {
                stopping = await HandleRequestAsync(pipe, services, cancellationToken);
            }
            catch (Exception ex)
            {
                RtLog.Error("[rt-daemon] unhandled exception in request loop: {Message}", ex.Message);
            }
            finally
            {
                pipe.Disconnect();
            }
        }

        DaemonState.Remove();
        return 0;
    }

    private static async Task<bool> HandleRequestAsync(NamedPipeServerStream pipe, IServiceProvider services, CancellationToken ct)
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

        if (request.WorkingDirectory is not null)
            Directory.SetCurrentDirectory(request.WorkingDirectory);

        var pipeConsole = new DaemonPipeConsole(writer, request.RequestId);
        int exitCode;
        try
        {
            Environment.ExitCode = 0;
            using (RtConsole.UseConsole(pipeConsole))
                exitCode = await App.RunAsync(request.Args, services);
            exitCode = Math.Max(exitCode, Environment.ExitCode);
        }
        catch (IOException)
        {
            // Client disconnected mid-stream — daemon continues.
            return false;
        }
        catch (Exception ex)
        {
            try
            {
                await RespondAsync(writer, new DaemonResponse(request.RequestId, "stderr",
                    Data: $"[rt-daemon] unhandled exception: {ex.Message}\n"));
            }
            catch (IOException) { }
            exitCode = 1;
        }

        try { await RespondAsync(writer, new DaemonResponse(request.RequestId, "exit", Code: exitCode)); }
        catch (IOException) { }
        return false;
    }

    private static async Task RespondAsync(StreamWriter writer, DaemonResponse response)
        => await writer.WriteLineAsync(JsonSerializer.Serialize(response, DaemonProtocol.JsonOpts));
}
