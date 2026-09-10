using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RoslynTools;
using RoslynTools.Core;
using RoslynTools.Daemon;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Feedback;
using RoslynTools.Rendering;
using RoslynTools.Services;
using RoslynTools.Trust;
using System.CommandLine;

/// <summary>
/// Main entry point for the Roslyn Daemon application.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Internal mode: spawned by `rt maintainer daemon start`, runs as the background pipe server.
        // The daemon installs DaemonPipeConsole per request, so do not pre-register Spectre.
        if (args.Length > 0 && args[0] == "--daemon-serve")
            return await DaemonServer.RunAsync(DaemonServer.ParseIdleTimeout(args));

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

        var typedDefinitions = host.Services
            .GetServices<ICommandApplicationPort>()
            .Where(port => port is not ILegacyCommandApplicationPort)
            .Select(port => port.Definition)
            .ToArray();

        // Route through the daemon if one is running and this isn't a daemon lifecycle command.
        if (!IsDaemonCommand(args)
            && !DaemonOwnedCommands.IsInvocation(args)
            && !TypedCommandInvocation.IsMatch(args, typedDefinitions)
            && !TypedCommandInvocation.IsLegacyMutationMatch(
                args,
                App.Commands.Select(command => command.Info))
            && !IsInformationalOrLocalProfileInvocation(args)
            && !DaemonRuntime.IsDisabled)
        {
            var client = host.Services.GetRequiredService<DaemonClient>();
            var exitCode = await client.TrySendAsync(args);
            if (exitCode is not null)
                return exitCode.Value;
        }

        var trusted = new Command(
            "trusted",
            "Explicit trusted-local experimental commands that may execute repository code");
        trusted.AddCommand(App.AllCommands
            .Single(command => command.Info.Name == "probe-di")
            .Build());

        var maintainer = new Command(
            "maintainer",
            "Dogfood, feedback, and release-maintenance commands");
        maintainer.AddCommand(DaemonCommand.Build());
        maintainer.AddCommand(FeedbackCommand.Build(
            host.Services.GetRequiredService<FeedbackAddOperation>(),
            host.Services.GetRequiredService<FeedbackListOperation>(),
            host.Services.GetRequiredService<FeedbackProcessOperation>(),
            host.Services.GetRequiredService<FeedbackReportOperation>()));

        var trust = TrustCommand.Build(
            host.Services.GetRequiredService<TrustAddOperation>(),
            host.Services.GetRequiredService<TrustListOperation>(),
            host.Services.GetRequiredService<TrustStatusOperation>(),
            host.Services.GetRequiredService<TrustRemoveOperation>());

        return await App.RunAsync(
            args,
            host.Services,
            [
                trusted,
                maintainer,
                trust
            ]);
    }

    private static bool IsDaemonCommand(string[] args)
    {
        return args.Length > 1 && args[0] == "maintainer" && args[1] == "daemon";
    }

    private static bool IsInformationalOrLocalProfileInvocation(string[] args)
    {
        return args.FirstOrDefault() is "trusted" or "maintainer" or "trust"
               || args.Any(arg => arg is "-h" or "--help" or "--version");
    }
}
