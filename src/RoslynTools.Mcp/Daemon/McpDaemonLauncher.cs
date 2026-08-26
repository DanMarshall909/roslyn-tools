using System.Diagnostics;
using System.Globalization;
using RoslynTools.Daemon;

namespace RoslynTools.Mcp.Daemon;

internal sealed record McpDaemonLaunchOptions(TimeSpan IdleTimeout)
{
    internal const string IdleTimeoutEnvironmentVariable = "RT_DAEMON_IDLE_TIMEOUT_SECONDS";
    internal static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(10);

    internal static McpDaemonLaunchOptions FromEnvironment(
        Func<string, string?> getEnvironmentVariable)
    {
        var configured = getEnvironmentVariable(IdleTimeoutEnvironmentVariable);
        return double.TryParse(
                   configured,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out var seconds)
               && seconds > 0
            ? new McpDaemonLaunchOptions(TimeSpan.FromSeconds(seconds))
            : new McpDaemonLaunchOptions(DefaultIdleTimeout);
    }
}

internal sealed record DaemonLaunchCommand(string FileName, IReadOnlyList<string> Arguments);

internal interface IMcpDaemonLauncher
{
    Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default);
}

/// <summary>Starts the CLI daemon without allowing its output to enter the MCP stdio protocol.</summary>
internal sealed class McpDaemonLauncher : IMcpDaemonLauncher
{
    private static readonly SemaphoreSlim StartGate = new(1, 1);
    private readonly McpDaemonLaunchOptions _options;

    internal McpDaemonLauncher()
        : this(McpDaemonLaunchOptions.FromEnvironment(Environment.GetEnvironmentVariable))
    {
    }

    internal McpDaemonLauncher(McpDaemonLaunchOptions options)
    {
        _options = options;
    }

    public async Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        if (await DaemonClient.IsPipeReachableAsync(cancellationToken))
            return true;

        await StartGate.WaitAsync(cancellationToken);
        try
        {
            if (await DaemonClient.IsPipeReachableAsync(cancellationToken))
                return true;

            DaemonLaunchCommand command;
            try
            {
                command = ResolveLaunchCommand(
                    AppContext.BaseDirectory,
                    OperatingSystem.IsWindows(),
                    _options.IdleTimeout);
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            var startInfo = new ProcessStartInfo(command.FileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in command.Arguments)
                startInfo.ArgumentList.Add(argument);

            Process? process;
            try
            {
                process = Process.Start(startInfo);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return false;
            }

            if (process is null)
                return false;

            _ = DrainProcessAsync(process);

            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(100, cancellationToken);
                if (await DaemonClient.IsPipeReachableAsync(cancellationToken))
                    return true;
            }

            return false;
        }
        finally
        {
            StartGate.Release();
        }
    }

    internal static DaemonLaunchCommand ResolveLaunchCommand(
        string baseDirectory,
        bool isWindows,
        TimeSpan idleTimeout)
    {
        var executable = Path.Combine(baseDirectory, isWindows ? "rt.exe" : "rt");
        var daemonArguments = new[]
        {
            "--daemon-serve",
            "--idle-timeout-seconds",
            idleTimeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)
        };

        if (File.Exists(executable))
            return new DaemonLaunchCommand(executable, daemonArguments);

        var assembly = Path.Combine(baseDirectory, "rt.dll");
        if (File.Exists(assembly))
            return new DaemonLaunchCommand("dotnet", [assembly, .. daemonArguments]);

        throw new InvalidOperationException(
            "Cannot locate the rt daemon executable beside roslyn-tools-mcp.");
    }

    private static async Task DrainProcessAsync(Process process)
    {
        using (process)
        {
            try
            {
                await Task.WhenAll(
                    process.StandardOutput.ReadToEndAsync(),
                    process.StandardError.ReadToEndAsync(),
                    process.WaitForExitAsync());
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                // Startup/retry decides availability; process observation is best effort.
            }
        }
    }
}
