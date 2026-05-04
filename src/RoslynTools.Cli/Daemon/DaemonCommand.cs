using System.CommandLine;
using System.Diagnostics;
using RoslynTools.Core;

namespace RoslynTools.Daemon;

internal static class DaemonCommand
{
    internal static Command Build()
    {
        var cmd = new Command("daemon", "Manage the rt background workspace daemon");
        cmd.AddCommand(BuildStart());
        cmd.AddCommand(BuildStop());
        cmd.AddCommand(BuildStatus());
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

            var exe = Environment.ProcessPath
                      ?? throw new InvalidOperationException(
                          "Cannot locate rt executable — use a published binary to start the daemon.");

            Process.Start(new ProcessStartInfo(exe, "--daemon-serve")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            // Poll until daemon.json appears — the server writes it atomically after the pipe is ready.
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
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

            RtLog.Warning("Daemon did not start within 5 seconds.");
        });
        return cmd;
    }

    private static Command BuildStop()
    {
        var cmd = new Command("stop", "Stop the running daemon");
        cmd.SetHandler(async () =>
        {
            var state = DaemonState.TryRead();
            if (state is null) { RtLog.Info("No daemon running."); return; }

            if (!DaemonState.IsAlive(state))
            {
                DaemonState.Remove();
                RtLog.Info("Daemon was not running. Cleaned up stale state.");
                return;
            }

            // Try clean protocol shutdown (works when versions match).
            var exitCode = await DaemonClient.TrySendAsync(["stop"]);
            if (exitCode is not null) { RtLog.Info("Daemon stopped."); return; }

            // Fall back to kill by PID (version mismatch, stale pipe, etc.).
            try
            {
                using var proc = Process.GetProcessById(state.Pid);
                proc.Kill();
                RtLog.Info("Daemon (PID {Pid}) killed.", state.Pid);
            }
            catch { RtLog.Warning("Could not kill PID {Pid}.", state.Pid); }

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
            if (state is null) { RtLog.Info("Daemon: STOPPED"); return; }

            var alive = DaemonState.IsAlive(state);
            var uptime = DateTimeOffset.UtcNow - state.StartedAt;
            var status = alive ? "RUNNING" : "STOPPED (stale)";

            RtLog.Info("Daemon:   {Status}", status);
            RtLog.Info("PID:      {Pid}", state.Pid);
            RtLog.Info("Pipe:     {Pipe}", @"\\.\pipe\" + state.PipeName);
            RtLog.Info("Uptime:   {Hours}h {Minutes}m {Seconds}s", (int)uptime.TotalHours, uptime.Minutes, uptime.Seconds);
            if (state.Solution is not null) RtLog.Info("Solution: {Solution}", state.Solution);
            RtLog.Info("Version:  {Version}", state.RtVersion);

            if (!alive)
                RtLog.Warning("State file exists but process is not running.");
        });
        return cmd;
    }
}
