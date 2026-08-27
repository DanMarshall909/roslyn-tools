using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;

namespace RoslynTools.Daemon.Rpc;

internal sealed record DaemonLaunchOptions(TimeSpan IdleTimeout)
{
    internal const string IdleTimeoutEnvironmentVariable = "RT_DAEMON_IDLE_TIMEOUT_SECONDS";
    internal static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(10);

    internal static DaemonLaunchOptions FromEnvironment(
        Func<string, string?> getEnvironmentVariable)
    {
        var configured = getEnvironmentVariable(IdleTimeoutEnvironmentVariable);
        return double.TryParse(
                   configured,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out var seconds)
               && seconds > 0
            ? new DaemonLaunchOptions(TimeSpan.FromSeconds(seconds))
            : new DaemonLaunchOptions(DefaultIdleTimeout);
    }
}

internal sealed record DaemonLaunchCommand(string FileName, IReadOnlyList<string> Arguments);

/// <summary>Starts the sibling rt daemon without allowing its output into a host protocol.</summary>
internal sealed class DaemonLauncher : IDaemonLauncher
{
    private static readonly SemaphoreSlim StartGate = new(1, 1);
    private readonly DaemonLaunchOptions _options;

    internal DaemonLauncher()
        : this(DaemonLaunchOptions.FromEnvironment(Environment.GetEnvironmentVariable))
    {
    }

    internal DaemonLauncher(DaemonLaunchOptions options)
    {
        _options = options;
    }

    public async Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        if (await IsPipeReachableAsync(cancellationToken))
            return true;

        await StartGate.WaitAsync(cancellationToken);
        try
        {
            if (await IsPipeReachableAsync(cancellationToken))
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
                if (await IsPipeReachableAsync(cancellationToken))
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

        throw new InvalidOperationException("Cannot locate the rt daemon executable beside this host.");
    }

    private static async Task<bool> IsPipeReachableAsync(CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            DaemonTransport.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(1));
            await pipe.ConnectAsync(deadline.Token);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            return false;
        }
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
                // Recovery decides availability; detached process observation is best effort.
            }
        }
    }
}
