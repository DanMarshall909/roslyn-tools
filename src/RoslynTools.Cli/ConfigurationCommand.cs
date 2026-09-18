using System.CommandLine;
using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools;

internal static class ConfigurationCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    internal static Command Build(RtConfigurationLoadResult loadResult)
    {
        var command = new Command(
            "config",
            "Show the global, repository, and effective rt configuration");
        command.SetHandler(context =>
        {
            if (!loadResult.IsSuccess)
            {
                RtLog.Error("{Error}", loadResult.Error);
                context.ExitCode = 2;
                return;
            }

            var configuration = loadResult.Configuration!;
            if (RtContext.Format == OutputFormat.Json)
            {
                RtConsole.WriteLine(
                    RtOutputContext.Result,
                    JsonSerializer.Serialize(new
                    {
                        global = configuration.GlobalFile,
                        repository = configuration.RepositoryFile,
                        configuration.RepositoryRoot,
                        daemon = new
                        {
                            configuration.Daemon.Enabled,
                            configuration.Daemon.AutoStart,
                            IdleTimeoutSeconds = configuration.Daemon.IdleTimeout.TotalSeconds
                        },
                        mcp = new
                        {
                            EnabledTools = configuration.Mcp.EnabledTools?.Order(StringComparer.Ordinal),
                            DisabledTools = configuration.Mcp.DisabledTools.Order(StringComparer.Ordinal)
                        }
                    }, JsonOptions));
                return;
            }

            RtLog.Info(
                "Global config:     {State} {Path}",
                configuration.GlobalFile.Exists ? "loaded" : "absent",
                configuration.GlobalFile.Path ?? "<unavailable>");
            RtLog.Info(
                "Repository config: {State} {Path}",
                configuration.RepositoryFile.Exists ? "loaded" : "absent",
                configuration.RepositoryFile.Path ?? "<not in a Git worktree>");
            RtLog.Info("Daemon enabled:    {Enabled}", configuration.Daemon.Enabled);
            RtLog.Info("Daemon auto-start: {AutoStart}", configuration.Daemon.AutoStart);
            RtLog.Info(
                "Daemon idle:       {Seconds}s",
                configuration.Daemon.IdleTimeout.TotalSeconds);
            RtLog.Info(
                "MCP enabled tools: {Tools}",
                configuration.Mcp.EnabledTools is null
                    ? "all otherwise eligible tools"
                    : string.Join(", ", configuration.Mcp.EnabledTools.Order(StringComparer.Ordinal)));
            RtLog.Info(
                "MCP disabled tools: {Tools}",
                configuration.Mcp.DisabledTools.Count == 0
                    ? "none"
                    : string.Join(", ", configuration.Mcp.DisabledTools.Order(StringComparer.Ordinal)));
        });
        return command;
    }
}
