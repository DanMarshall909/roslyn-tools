using Microsoft.Extensions.DependencyInjection;
using RoslynTools;
using RoslynTools.Core;
using RoslynTools.Mcp;

if (!McpHostOptions.TryParse(args, out var hostOptions, out var optionError))
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
    return 0;
}

var services = new ServiceCollection();
Bootstrap.RegisterServices(services);
services.AddMcpServices(RtConsole.In, RtConsole.Out, hostOptions);

using var provider = services.BuildServiceProvider();
var server = provider.GetRequiredService<Server>();

await server.RunAsync();
return 0;
