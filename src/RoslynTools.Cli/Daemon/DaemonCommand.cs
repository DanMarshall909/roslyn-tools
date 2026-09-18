using System.CommandLine;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;

namespace RoslynTools.Daemon;

/// <summary>
/// A command for daemon operations.
/// </summary>
internal static class DaemonCommand
{
    /// <summary>
    /// A method to build a daemon command.
    /// </summary>
    internal static Command Build()
    {
        var cmd = new Command(
            "daemon",
            "Manage the experimental background daemon (routing is controlled by rt configuration or RT_DAEMON_EXPERIMENT)");
        cmd.AddCommand(BuildStart());
        cmd.AddCommand(BuildStop());
        cmd.AddCommand(BuildStatus());
        cmd.AddCommand(BuildLogs());
        return cmd;
    }

    private static Command BuildStart()
    {
        var cmd = new Command("start", "Start the daemon (loads workspace in background)");
        cmd.SetHandler(async () =>
        {
            var state = DaemonState.TryRead();
            if (state is not null && DaemonState.IsAlive(state))
            {
                RtLog.Info("Daemon already running (PID {Pid}).", state.Pid);
                return;
            }

            // No (valid) state file. Before spawning, see if an orphan daemon already owns
            // the pipe — spawning a second one would just crash on the pipe-bind check.
            if (await DaemonClient.IsPipeReachableAsync())
            {
                RtLog.Warning("A daemon is already running but its state file is missing. Run `rt maintainer daemon stop` first.");
                return;
            }

            var exe = Environment.ProcessPath
                      ?? throw new InvalidOperationException(
                          "Cannot locate rt executable — use a published binary to start the daemon.");

            var startInfo = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--daemon-serve");
            startInfo.ArgumentList.Add("--idle-timeout-seconds");
            startInfo.ArgumentList.Add(
                DaemonRuntime.IdleTimeout.TotalSeconds.ToString(
                    "0.###",
                    System.Globalization.CultureInfo.InvariantCulture));
            Process.Start(startInfo);

            // Poll until daemon.json appears — the server writes it after MSBuild + DI init,
            // so its presence is a real readiness signal, not just "process spawned".
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100);
                var fresh = DaemonState.TryRead();
                if (fresh is not null && DaemonState.IsAlive(fresh))
                {
                    RtLog.Info("Daemon ready. PID {Pid}", fresh.Pid);
                    return;
                }
            }

            RtLog.Warning("Daemon did not start within 10 seconds.");
        });
        return cmd;
    }

    private static Command BuildStop()
    {
        var cmd = new Command("stop", "Stop the running daemon");
        cmd.SetHandler(async ctx =>
        {
            var sp = ctx.BindingContext.GetService<IServiceProvider>();
            var client = sp?.GetService<DaemonClient>();
            var state = DaemonState.TryRead();

            // Always try a clean pipe-based stop first — works for both tracked daemons and
            // orphans (state file missing but process alive). TrySendDirectAsync ignores state.
            // Only treat exit code 0 as success; anything else (e.g. a BOM-mismatched daemon
            // returning 1 on deserialize failure) means the daemon is broken — fall through
            // to the kill-by-PID path below.
            if (client is not null)
            {
                var exitCode = await client.TrySendDirectAsync(["stop"]);
                if (exitCode == 0)
                {
                    await WaitForProcessExitAsync(state?.Pid);
                    DaemonState.Remove();
                    RtLog.Info("Daemon stopped.");
                    return;
                }
            }

            if (state is null)
            {
                RtLog.Info("No daemon running.");
                return;
            }

            if (!DaemonState.IsAlive(state))
            {
                DaemonState.Remove();
                RtLog.Info("Daemon was not running. Cleaned up stale state.");
                return;
            }

            // State file present, process alive, pipe didn't respond — kill by PID.
            try
            {
                using var proc = Process.GetProcessById(state.Pid);
                using var killCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                proc.Kill();
                await proc.WaitForExitAsync(killCts.Token);
                RtLog.Info("Daemon (PID {Pid}) killed.", state.Pid);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception
                                           or NotSupportedException)
            {
                RtLog.Warning("Could not kill PID {Pid}: {Reason}.", state.Pid, ex.Message);
            }

            DaemonState.Remove();
        });
        return cmd;
    }

    private static Command BuildStatus()
    {
        var cmd = new Command("status", "Show daemon status");
        cmd.SetHandler(() =>
        {
            var state = DaemonState.TryRead();
            if (state is null)
            {
                RtLog.Info("Daemon: STOPPED");
                return;
            }

            var alive = DaemonState.IsAlive(state);
            var uptime = DateTimeOffset.UtcNow - state.StartedAt;
            var status = alive ? "RUNNING" : "STOPPED (stale)";

            RtLog.Info("Daemon:   {Status}", status);
            RtLog.Info("PID:      {Pid}", state.Pid);
            RtLog.Info("Pipe:     {Pipe}", DaemonEncoding.DisplayPipePath(state.PipeName));
            RtLog.Info("Uptime:   {Hours}h {Minutes}m {Seconds}s", (int)uptime.TotalHours, uptime.Minutes,
                uptime.Seconds);
            if (state.Solution is not null) RtLog.Info("Solution: {Solution}", state.Solution);
            RtLog.Info("Version:  {Version}", state.RtVersion);

            if (!alive)
                RtLog.Warning("State file exists but process is not running.");
        });
        return cmd;
    }

    // Waits up to 5 seconds for the given PID to exit. Returns silently if the PID is unknown,
    // already gone, or doesn't exit in time — caller will report success based on its own
    // protocol-level signal (the daemon ACK'd the stop). The wait matters because the daemon's
    // exe is the SAME file rt clients run from; rebuilds racing the daemon's actual exit see
    // "file in use" errors.
    private static async Task WaitForProcessExitAsync(int? pid)
    {
        if (pid is null) return;
        try
        {
            using var proc = Process.GetProcessById(pid.Value);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (ArgumentException)
        {
            /* already gone */
        }
        catch (OperationCanceledException)
        {
            /* timed out — caller still reports success */
        }
    }

    private static Command BuildLogs()
    {
        var cmd = new Command("logs", "Print the daemon activity log");
        cmd.SetHandler(() =>
        {
            if (!File.Exists(DaemonState.LogFile))
            {
                RtLog.Info("No daemon log found.");
                return;
            }

            using var fs = new FileStream(DaemonState.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            RtConsole.Write(RtOutputContext.Result, reader.ReadToEnd());
        });
        return cmd;
    }
}
