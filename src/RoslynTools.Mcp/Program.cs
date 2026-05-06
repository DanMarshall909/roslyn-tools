using Microsoft.Extensions.DependencyInjection;
using RoslynTools;
using RoslynTools.Core;
using RoslynTools.Mcp;

MsBuildBootstrap.Register();

var services = new ServiceCollection();
Bootstrap.RegisterServices(services);
services.AddMcpServices(RtConsole.In, RtConsole.Out);

using var provider = services.BuildServiceProvider();
var server = provider.GetRequiredService<Server>();

await server.RunAsync();
