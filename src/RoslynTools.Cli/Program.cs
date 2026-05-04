using Microsoft.Extensions.Hosting;
using RoslynTools;
using RoslynTools.Core;
using RoslynTools.Daemon;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Fast path: `rt dump-help` only enumerates the command registry in-process and emits
        // Markdown — it doesn't load an MSBuild workspace. Skip MSBuild registration and host
        // construction so the post-build target that regenerates docs/COMMANDS.md stays fast.
        if (args.Length > 0 && args[0] == "dump-help")
        {
            // Force UTF-8 so em-dashes and other Unicode in RtCommandInfo strings survive
            // `cmd > file` redirection on Windows (default console encoding is often cp1252).
            RtConsole.SetOutputEncoding(System.Text.Encoding.UTF8);
            return await App.RunAsync(args);
        }

        // Internal mode: spawned by `rt daemon start`, runs as the background pipe server.
        if (args.Length > 0 && args[0] == "--daemon-serve")
            return await DaemonServer.RunAsync();

        // Route through the daemon if one is running and this isn't a daemon lifecycle command.
        if (!IsDaemonCommand(args) && !NoDaemon)
        {
            var exitCode = await DaemonClient.TrySendAsync(args);
            if (exitCode is not null)
                return exitCode.Value;
        }

        MsBuildBootstrap.Register();

        using var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(Bootstrap.RegisterServices)
            .Build();

        return await App.RunAsync(args, host.Services, [DaemonCommand.Build()]);
    }

    private static bool IsDaemonCommand(string[] args) =>
        args.Length > 0 && args[0] == "daemon";

    private static bool NoDaemon =>
        Environment.GetEnvironmentVariable("RT_NO_DAEMON") is "1" or "true";
}
