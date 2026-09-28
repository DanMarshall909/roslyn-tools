using Microsoft.Extensions.DependencyInjection;
using RoslynTools;
using RoslynTools.Configuration;
using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp;

var configuration = new HostConfigurationLoader().ResolveCurrent(Directory.GetCurrentDirectory());
if (!configuration.IsValid)
{
    foreach (var diagnostic in configuration.Diagnostics)
        RtLog.Error("{Message}", diagnostic.Message);
    return 2;
}

if (!McpHostOptions.TryParse(args, configuration, out var hostOptions, out var optionError))
{
    RtLog.Error("{Error}", optionError);
    RtLog.Info("Usage: roslyn-tools-mcp [--trust-repository <absolute-repository-root>]");
    return 2;
}

if (hostOptions.ShowHelp)
{
    RtLog.Info("Usage: roslyn-tools-mcp [--trust-repository <absolute-repository-root>]");
    RtLog.Info(
        "--trust-repository authorizes this host process to evaluate solutions beneath the reviewed absolute repository root. MCP tool arguments cannot grant trust.");
    RtLog.Info(
        "Without that compatibility option, the host uses repositories approved by `rt trust add <repository>`.");
    return 0;
}

var services = new ServiceCollection();
services.AddSingleton(configuration);
Bootstrap.RegisterServices(services);
services.AddMcpServices(RtConsole.In, RtConsole.Out, hostOptions);

using var provider = services.BuildServiceProvider();
var validatedConfiguration = McpConfigurationValidator.Validate(
    configuration,
    provider.GetRequiredService<ApplicationCommandCatalog>());
if (!validatedConfiguration.IsValid)
{
    foreach (var diagnostic in validatedConfiguration.Diagnostics)
        RtLog.Error("{Message}", diagnostic.Message);
    return 2;
}

using var daemonConfiguration = DaemonRuntime.UseExperimentOverride(
    configuration.Effective.Daemon.Enabled);
var server = provider.GetRequiredService<Server>();

await server.RunAsync();
return 0;
