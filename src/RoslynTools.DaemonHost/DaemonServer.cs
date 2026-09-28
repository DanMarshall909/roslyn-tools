using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;

namespace RoslynTools.Daemon;

/// <summary>
/// The server for daemon communication.
/// </summary>
internal static class DaemonServer
{
    internal const string PipeName = DaemonTransport.PipeName;

    internal static TimeSpan? ParseIdleTimeout(string[] args)
    {
        const string option = "--idle-timeout-seconds";
        for (var index = 1; index < args.Length; index++)
        {
            if (!string.Equals(args[index], option, StringComparison.Ordinal))
                continue;

            if (index + 1 >= args.Length
                || !double.TryParse(
                    args[index + 1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var seconds)
                || seconds <= 0)
                throw new ArgumentException($"{option} requires a positive number.");

            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    /// <summary>
    /// A method to start the daemon server.
    /// </summary>
    internal static async Task<int> RunAsync(TimeSpan? idleTimeout = null)
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
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
        }
        catch (IOException ex)
        {
            await log.WriteLineAsync(
                $"[{Now()}] FATAL pipe bind failed: {ex.Message} — another daemon already owns the pipe.");
            return 1;
        }

        using var pipeOwn = pipe;

        using var cts = new CancellationTokenSource();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
            }
        };
        RtConsole.AddCancelKeyPressHandler((_, e) =>
        {
            e.Cancel = true;
            try
            {
                cts.Cancel();
            }
            catch
            {
            }
        });

        // Initialize MSBuild and DI BEFORE writing the state file. `rt maintainer daemon start` polls
        // the state file as the readiness signal — writing it earlier would make it report
        // "ready" while the daemon was still loading MSBuild, and any request landing in that
        // window would block on ConnectAsync (pipe bound, but WaitForConnectionAsync not yet
        // called) and silently fall back to in-process.
        IHost host;
        try
        {
            var registeredInstance = MsBuildBootstrap.RegisterForDaemon(Environment.CurrentDirectory);
            var registeredIdentity = MsBuildInstanceIdentity.From(registeredInstance);
            host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    DaemonHostServiceRegistration.RegisterServices(services, registeredIdentity);
                })
                .Build();
        }
        catch (Exception ex)
        {
            await log.WriteLineAsync($"[{Now()}] FATAL initializing services: {ex}");
            return 1;
        }

        DaemonState.Write(new DaemonStateFile(
            Environment.ProcessId,
            PipeName,
            null,
            null,
            false,
            null,
            DateTimeOffset.UtcNow,
            DaemonState.CurrentVersion()));

        await log.WriteLineAsync($"[{Now()}] daemon ready");
        RtLog.Info("[rt-daemon] PID {Pid} listening on {Pipe}", Environment.ProcessId,
            DaemonEncoding.DisplayPipePath(PipeName));

        using (host)
        {
            var services = host.Services;
            var idleLifetime = idleTimeout is null
                ? null
                : new DaemonIdleLifetime(idleTimeout.Value);

            var stopping = false;
            while (!stopping && !cts.IsCancellationRequested)
            {
                try
                {
                    var connected = idleLifetime is null
                        ? await WaitForConnectionAsync(pipe, cts.Token)
                        : await idleLifetime.WaitForActivityAsync(
                            token => WaitForConnectionAsync(pipe, token),
                            cts.Token);
                    if (!connected)
                    {
                        await log.WriteLineAsync(
                            $"[{Now()}] daemon idle for {idleTimeout}; stopping");
                        break;
                    }
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

    private static async Task<bool> WaitForConnectionAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        await pipe.WaitForConnectionAsync(cancellationToken);
        return true;
    }

    private static async Task<bool> HandleRequestAsync(NamedPipeServerStream pipe, IServiceProvider services,
        StreamWriter log, CancellationToken ct)
    {
        // leaveOpen: true — pipe stream outlives the reader/writer wrappers.
        // DaemonEncoding.NoBom is critical: Encoding.UTF8 emits a 3-byte BOM on first flush,
        // and System.Text.Json on the receiving end rejects 0xEF as an invalid value.
        using var reader = new StreamReader(pipe, DaemonEncoding.NoBom, false, 1024, true);
        var writer = new StreamWriter(pipe, DaemonEncoding.NoBom, 1024, true) { AutoFlush = true };

        var line = await reader.ReadLineAsync(ct);
        if (line is null) return false;

        await log.WriteLineAsync($"[{Now()}] read {line.Length} chars");

        using var document = JsonDocument.Parse(line);
        if (document.RootElement.TryGetProperty("method", out var method)
            && method.GetString() == DaemonRpcProtocol.ExecuteCommandMethod)
        {
            var rpcRequest = JsonSerializer.Deserialize<DaemonRpcRequest>(line, DaemonProtocol.JsonOpts);
            if (rpcRequest is null)
                return false;

            var dispatcher = services.GetRequiredService<DaemonCommandDispatcher>();
            var rpcResponse = await dispatcher.ExecuteAsync(rpcRequest, ct);
            await writer.WriteLineAsync(JsonSerializer.Serialize(rpcResponse, DaemonProtocol.JsonOpts));
            return false;
        }

        DaemonRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<DaemonRequest>(line, DaemonProtocol.JsonOpts);
        }
        catch (Exception ex)
        {
            await log.WriteLineAsync($"[{Now()}] deserialize failed: {ex.Message}");
            await RespondAsync(writer, new DaemonResponse("?", "exit", Code: 1));
            return false;
        }

        if (request is null)
        {
            await RespondAsync(writer, new DaemonResponse("?", "exit", Code: 1));
            return false;
        }

        return await HandleLegacyControlRequestAsync(request, writer);
    }

    internal static async Task<bool> HandleLegacyControlRequestAsync(
        DaemonRequest request,
        TextWriter writer)
    {
        if (request.Args is ["ping"])
        {
            await RespondAsync(writer, new DaemonResponse(request.RequestId, "stdout", "pong\n"));
            await RespondAsync(writer, new DaemonResponse(request.RequestId, "exit", Code: 0));
            return false;
        }

        if (request.Args is ["stop"] or ["daemon", "stop"])
        {
            await RespondAsync(writer, new DaemonResponse(request.RequestId, "stdout", "Daemon stopping.\n"));
            await RespondAsync(writer, new DaemonResponse(request.RequestId, "exit", Code: 0));
            return true;
        }

        await RespondAsync(writer, new DaemonResponse(
            request.RequestId,
            "stderr",
            "Legacy daemon application arguments are not supported; use typed RPC.\n"));
        await RespondAsync(writer, new DaemonResponse(request.RequestId, "exit", Code: 1));
        return false;
    }

    private static string Now()
    {
        return DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff",
            CultureInfo.InvariantCulture);
    }

    private static async Task RespondAsync(TextWriter writer, DaemonResponse response)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, DaemonProtocol.JsonOpts));
    }
}
