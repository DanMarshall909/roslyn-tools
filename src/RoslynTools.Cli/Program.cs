using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RoslynTools;
using RoslynTools.Core;
using RoslynTools.Daemon;
using RoslynTools.Feedback;
using RoslynTools.Rendering;

/// <summary>
/// Main entry point for the Roslyn Daemon application.
/// </summary>
internal static class Program
{
    private static bool NoDaemon =>
        Environment.GetEnvironmentVariable("RT_NO_DAEMON") is "1" or "true";

    private static async Task<int> Main(string[] args)
    {
        // Internal mode: spawned by `rt daemon start`, runs as the background pipe server.
        // The daemon installs DaemonPipeConsole per request, so do not pre-register Spectre.
        if (args.Length > 0 && args[0] == "--daemon-serve")
            return await DaemonServer.RunAsync();

        // Install the CLI-side Spectre.Console adapter so every RtConsole call from
        // here on renders with colour + syntax highlighting. The override propagates
        // through the async tree via AsyncLocal for the lifetime of this process.
        // MCP-side callers (CliCommandBridge) install their own CapturingConsole *after*
        // entering App.RunAsync directly, bypassing this entry point.
        using var consoleScope = RtConsole.UseConsole(new SpectreRtConsole());

        // Fast path: `rt dump-help` only enumerates the command registry in-process and emits
        // Markdown — it doesn't load an MSBuild workspace. Skip MSBuild registration and host
        // construction so the post-build target that regenerates docs/COMMANDS.md stays fast.
        if (args.Length > 0 && args[0] == "dump-help")
        {
            // Force UTF-8 so em-dashes and other Unicode in RtCommandInfo strings survive
            // `cmd > file` redirection on Windows (default console encoding is often cp1252).
            RtConsole.SetOutputEncoding(Encoding.UTF8);
            return await App.RunAsync(args);
        }

        MsBuildBootstrap.Register();

        using var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(Bootstrap.RegisterServices)
            .Build();

        // Route through the daemon if one is running and this isn't a daemon lifecycle command.
        if (!IsDaemonCommand(args) && !DaemonOwnedCommands.IsInvocation(args) && !NoDaemon)
        {
            var client = host.Services.GetRequiredService<DaemonClient>();
            var exitCode = await client.TrySendAsync(args);
            if (exitCode is not null)
                return exitCode.Value;
        }

        return await App.RunAsync(
            args,
            host.Services,
            [
                DaemonCommand.Build(),
                FeedbackCommand.Build(
                    host.Services.GetRequiredService<FeedbackAddOperation>(),
                    host.Services.GetRequiredService<FeedbackListOperation>(),
                    host.Services.GetRequiredService<FeedbackProcessOperation>())
            ]);
    }

    private static bool IsDaemonCommand(string[] args)
    {
        return args.Length > 0 && args[0] == "daemon";
    }
}
