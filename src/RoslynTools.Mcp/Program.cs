using RoslynTools;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;

MsBuildBootstrap.Register();

var toolRegistry = new ToolRegistry();
var server = new Server(toolRegistry, Console.In, Console.Out);

await server.RunAsync();
