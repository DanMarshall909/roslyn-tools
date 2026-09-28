using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RoslynTools;
using RoslynTools.Configuration;
using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Daemon;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Feedback;
using RoslynTools.Rendering;
using RoslynTools.Services;
using System.CommandLine;

/// <summary>
/// Main entry point for the Roslyn Daemon application.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Internal mode: spawned by `rt maintainer daemon start`, runs as the background pipe server.
        // The daemon handles typed RPC without terminal rendering, so do not pre-register Spectre.
        if (args.Length > 0 && args[0] == "--daemon-serve")
            return await DaemonServer.RunAsync(DaemonServer.ParseIdleTimeout(args));

        // Install the CLI-side Spectre.Console adapter so every RtConsole call from
        // here on renders with colour + syntax highlighting. The override propagates
        // through the async tree via AsyncLocal for the lifetime of this process.
        // The MCP host has its own entry point and invokes application ports directly.
        using var consoleScope = RtConsole.UseConsole(new SpectreRtConsole());

        var invocationDirectory = Directory.GetCurrentDirectory();
        var configuration = new HostConfigurationLoader().ResolveCurrent(invocationDirectory);
        var isConfigInvocation = CliInvocationClassifier.IsConfigurationInspection(args);
        if (!configuration.IsValid && !isConfigInvocation)
        {
            foreach (var diagnostic in configuration.Diagnostics)
                RtLog.Error("{Message}", diagnostic.Message);
            return 2;
        }

        using var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                services.AddSingleton(configuration);
                Bootstrap.RegisterServices(services);
            })
            .Build();

        var validatedConfiguration = McpConfigurationValidator.Validate(
            configuration,
            host.Services.GetRequiredService<ApplicationCommandCatalog>());
        if (!validatedConfiguration.IsValid && !isConfigInvocation)
        {
            foreach (var diagnostic in validatedConfiguration.Diagnostics)
                RtLog.Error("{Message}", diagnostic.Message);
            return 2;
        }

        // Fast path: `rt dump-help` only enumerates the command registry in-process and emits
        // Markdown. It validates host configuration, but never registers MSBuild or routes
        // through the daemon.
        if (args.Length > 0 && args[0] == "dump-help")
        {
            // Force UTF-8 so em-dashes and other Unicode in RtCommandInfo strings survive
            // `cmd > file` redirection on Windows (default console encoding is often cp1252).
            RtConsole.SetOutputEncoding(Encoding.UTF8);
            return await App.RunAsync(args, host.Services);
        }

        if (!isConfigInvocation && CliInvocationClassifier.RequiresEagerMsBuildRegistration(args))
            MsBuildBootstrap.Register();

        using var daemonConfiguration = DaemonRuntime.UseExperimentOverride(
            configuration.Effective.Daemon.Enabled);

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

        return await App.RunAsync(
            args,
            host.Services,
            [
                trusted,
                maintainer
            ]);
    }

}
