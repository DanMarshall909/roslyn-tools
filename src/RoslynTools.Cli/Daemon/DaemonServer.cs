using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using RoslynTools.Core;

namespace RoslynTools.Daemon;

internal static class DaemonServer
{
    internal const string PipeName = "rt-daemon";

    internal static async Task<int> RunAsync()
    {
        Directory.CreateDirectory(DaemonState.StateDir);

        // Open the log file FIRST. The daemon process is spawned with CreateNoWindow=true
        // so Console.Out/Error go nowhere — the log file is the only durable record of
        // bind failures, init failures, and normal lifecycle events. FileShare.ReadWrite so
        // `rt daemon logs` (and a tail tool) can read while we hold it, and a competing
        // daemon can also append cleanly without locking us out.
        var logStream = new FileStream(DaemonState.LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await using var log = new StreamWriter(logStream, Encoding.UTF8) { AutoFlush = true };
        await log.WriteLineAsync($"[{Now()}] daemon starting PID {Environment.ProcessId}");

        NamedPipeServerStream pipe;
        try
        {
            pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                transmissionMode: PipeTransmissionMode.Byte,
                options: PipeOptions.Asynchronous);
        }
        catch (IOException ex)
        {
            await log.WriteLineAsync($"[{Now()}] FATAL pipe bind failed: {ex.Message} — another daemon already owns the pipe.");
            return 1;
        }

        using var pipeOwn = pipe;

        using var cts = new CancellationTokenSource();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { cts.Cancel(); } catch { } };
        RtConsole.AddCancelKeyPressHandler((_, e) => { e.Cancel = true; try { cts.Cancel(); } catch { } });

        // Initialize MSBuild and DI BEFORE writing the state file. `rt daemon start` polls
        // the state file as the readiness signal — writing it earlier would make it report
        // "ready" while the daemon was still loading MSBuild, and any request landing in that
        // window would block on ConnectAsync (pipe bound, but WaitForConnectionAsync not yet
        // called) and silently fall back to in-process.
        IHost host;
        try
        {
            MsBuildBootstrap.Register();
            host = Host.CreateDefaultBuilder()
                .ConfigureServices(Bootstrap.RegisterServices)
                .Build();
        }
        catch (Exception ex)
        {
            await log.WriteLineAsync($"[{Now()}] FATAL initializing services: {ex}");
            return 1;
        }

        DaemonState.Write(new DaemonStateFile(
            Pid: Environment.ProcessId,
            PipeName: PipeName,
            Solution: null,
            CachedHead: null,
            CachedDirty: false,
            CachedIndexMtime: null,
            StartedAt: DateTimeOffset.UtcNow,
            RtVersion: DaemonState.CurrentVersion()));

        await log.WriteLineAsync($"[{Now()}] daemon ready");
        RtLog.Info("[rt-daemon] PID {Pid} listening on {Pipe}", Environment.ProcessId, DaemonEncoding.DisplayPipePath(PipeName));

        using (host)
        {
            var services = host.Services;

            var stopping = false;
            while (!stopping && !cts.IsCancellationRequested)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    stopping = await HandleRequestAsync(pipe, services, log, cts.Token);
                }
                catch (Exception ex)
                {
                    RtLog.Error("[rt-daemon] unhandled exception in request loop: {Message}", ex.Message);
                    await log.WriteLineAsync($"[{Now()}] ERROR {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    if (pipe.IsConnected) pipe.Disconnect();
                }
            }
        }

        await log.WriteLineAsync($"[{Now()}] daemon stopped");
        DaemonState.Remove();
        return 0;
    }

    private static async Task<bool> HandleRequestAsync(NamedPipeServerStream pipe, IServiceProvider services, StreamWriter log, CancellationToken ct)
    {
        // leaveOpen: true — pipe stream outlives the reader/writer wrappers.
        // DaemonEncoding.NoBom is critical: Encoding.UTF8 emits a 3-byte BOM on first flush,
        // and System.Text.Json on the receiving end rejects 0xEF as an invalid value.
        using var reader = new StreamReader(pipe, DaemonEncoding.NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var writer = new StreamWriter(pipe, DaemonEncoding.NoBom, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };

        var line = await reader.ReadLineAsync(ct);
        if (line is null) return false;

        // Peek the raw bytes — invaluable when the request can't be parsed (BOM, encoding, etc.)
        // because without this you only see "deserialize failed" with no context on what came in.
        await log.WriteLineAsync($"[{Now()}] read {line.Length} chars: {(line.Length > 200 ? line[..200] + "..." : line)}");

        DaemonRequest? request;
        try { request = JsonSerializer.Deserialize<DaemonRequest>(line, DaemonProtocol.JsonOpts); }
        catch (Exception ex)
        {
            await log.WriteLineAsync($"[{Now()}] deserialize failed: {ex.Message}");
            await RespondAsync(writer, new DaemonResponse("?", "exit", Code: 1));
            return false;
        }

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

        var argsDisplay = string.Join(" ", request.Args);
        await log.WriteLineAsync($"[{Now()}] → {argsDisplay}");
        var sw = Stopwatch.StartNew();

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
            await log.WriteLineAsync($"[{Now()}] ← client disconnected after {sw.ElapsedMilliseconds}ms");
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

        await log.WriteLineAsync($"[{Now()}] ← exit {exitCode} in {sw.ElapsedMilliseconds}ms");

        try { await RespondAsync(writer, new DaemonResponse(request.RequestId, "exit", Code: exitCode)); }
        catch (IOException) { }
        return false;
    }

    private static string Now() => DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task RespondAsync(StreamWriter writer, DaemonResponse response)
        => await writer.WriteLineAsync(JsonSerializer.Serialize(response, DaemonProtocol.JsonOpts));
}
