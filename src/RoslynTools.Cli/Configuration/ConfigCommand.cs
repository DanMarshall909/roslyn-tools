using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Cli;
using RoslynTools.Configuration;
using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Core.Output;

namespace RoslynTools.Commands;

/// <summary>Reports selected host configuration without evaluating repository build inputs.</summary>
internal sealed class ConfigCommand(
    HostConfigurationResolution resolution,
    IServiceProvider services)
    : IInspectCommand, IStructuredOutputCommand
{
    private readonly HostConfigurationResolution _resolution = resolution;
    private readonly IServiceProvider _services = services;

    public static RtCommandInfo Info => HostCommandMetadata.Config;

    public Command Build()
    {
        var command = new Command(
            "config",
            "Show selected configuration files, effective daemon settings, and MCP policy");
        command.SetHandler(context =>
        {
            var resolution = McpConfigurationValidator.Validate(
                _resolution,
                _services.GetRequiredService<ApplicationCommandCatalog>());
            if (RtContext.Format == OutputFormat.Json)
            {
                RtResultRenderer.Write(new RtResult<HostConfigurationResolution>(
                    resolution.IsValid ? RtResultStatus.Ok : RtResultStatus.Failed,
                    resolution,
                    [],
                    [],
                    resolution.IsValid ? 0 : 1));
            }
            else
            {
                RenderText(resolution);
            }

            if (!resolution.IsValid)
                context.ExitCode = 1;
        });
        return command;
    }

    private static void RenderText(HostConfigurationResolution resolution)
    {
        RtConsole.WriteLine(
            RtOutputContext.Result,
            $"Global: {Describe(resolution.GlobalFile)}");
        RtConsole.WriteLine(
            RtOutputContext.Result,
            $"Repository: {Describe(resolution.RepositoryFile)}");
        if (resolution.IsValid)
        {
            var daemon = resolution.Effective.Daemon;
            RtConsole.WriteLine(
                RtOutputContext.Result,
                $"Daemon: enabled={daemon.Enabled} ({daemon.EnabledSource}), "
                + $"autoStart={daemon.AutoStart} ({daemon.AutoStartSource}), "
                + $"idleTimeoutSeconds={daemon.IdleTimeoutSeconds:0.###} ({daemon.IdleTimeoutSource})");
            var enabled = resolution.Effective.Mcp.EnabledTools is null
                ? "unrestricted"
                : string.Join(",", resolution.Effective.Mcp.EnabledTools);
            RtConsole.WriteLine(
                RtOutputContext.Result,
                $"MCP: enabledTools={enabled}; disabledTools="
                + string.Join(",", resolution.Effective.Mcp.DisabledTools));
            return;
        }

        foreach (var diagnostic in resolution.Diagnostics)
            RtConsole.WriteLine(RtOutputContext.Result, diagnostic.Message);
    }

    private static string Describe(HostConfigurationFileState state) => state.Path is null
        ? "not applicable"
        : !state.Exists
            ? $"absent ({state.Path})"
            : state.IsValid
                ? $"valid ({state.Path})"
                : $"invalid ({state.Path})";
}
