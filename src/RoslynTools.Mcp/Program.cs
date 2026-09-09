using Microsoft.Extensions.DependencyInjection;
using RoslynTools;
using RoslynTools.Core;
using RoslynTools.Mcp;

if (!McpHostOptions.TryParse(args, out var hostOptions, out var optionError))
{
    RtLog.Error("{Error}", optionError);
    RtLog.Info("Usage: roslyn-tools-mcp [--trust-repository <absolute-repository-root>]... [--use-trusted-roots]");
    return 2;
}

if (hostOptions.ShowHelp)
{
    RtLog.Info("Usage: roslyn-tools-mcp [--trust-repository <absolute-repository-root>]... [--use-trusted-roots]");
    RtLog.Info(
        "Repeat --trust-repository to authorize this host process to evaluate solutions beneath each reviewed absolute repository root. Ordinary MCP tool arguments cannot grant trust.");
    RtLog.Info("--use-trusted-roots opts into user-owned saved approvals, reloaded for each request. CI and one-shot CLI trust do not require this registry.");
    return 0;
}

var services = new ServiceCollection();
Bootstrap.RegisterServices(services);
services.AddMcpServices(RtConsole.In, RtConsole.Out, hostOptions);

using var provider = services.BuildServiceProvider();
var server = provider.GetRequiredService<Server>();

await server.RunAsync();
return 0;
