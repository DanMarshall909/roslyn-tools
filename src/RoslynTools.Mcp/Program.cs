using Microsoft.Extensions.DependencyInjection;
using RoslynTools;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;

if (!McpHostOptions.TryParse(args, out var parsedOptions, out var optionError))
{
    RtLog.Error("{Error}", optionError);
    RtLog.Info("Usage: roslyn-tools-mcp [--trust-repository <absolute-repository-root>]");
    return 2;
}

if (parsedOptions.ShowHelp)
{
    RtLog.Info("Usage: roslyn-tools-mcp [--trust-repository <absolute-repository-root>]");
    RtLog.Info(
        "--trust-repository authorizes this host process to evaluate solutions beneath the reviewed absolute repository root. MCP tool arguments cannot grant trust.");
    RtLog.Info(
        "Without that compatibility option, the host uses repositories approved by `rt trust add <repository>`.");
    return 0;
}

var configurationResult = RtConfigurationLoader.Load(
    parsedOptions.TrustedRepositoryRoot ?? Directory.GetCurrentDirectory(),
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    Environment.GetEnvironmentVariable);
if (!configurationResult.IsSuccess)
{
    RtLog.Error("{Error}", configurationResult.Error);
    return 2;
}

var configuration = configurationResult.Configuration!;
using var daemonConfiguration = DaemonRuntime.UseConfiguration(configuration.Daemon);
var hostOptions = parsedOptions.WithToolPolicy(configuration.Mcp);

var services = new ServiceCollection();
Bootstrap.RegisterServices(services);
services.AddMcpServices(RtConsole.In, RtConsole.Out, hostOptions);

using var provider = services.BuildServiceProvider();
var policyError = McpToolAvailability.ValidatePolicy(
    provider.GetRequiredService<CommandDescriptorRegistry>().All,
    hostOptions.ToolPolicy);
if (policyError is not null)
{
    RtLog.Error("{Error}", policyError);
    return 2;
}

var server = provider.GetRequiredService<Server>();

await server.RunAsync();
return 0;
