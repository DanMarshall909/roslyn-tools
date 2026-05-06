using RoslynTools;
using RoslynTools.Core;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;

MsBuildBootstrap.Register();

var toolRegistry = new ToolRegistry();
var server = new Server(toolRegistry, RtConsole.In, RtConsole.Out);

await server.RunAsync();
