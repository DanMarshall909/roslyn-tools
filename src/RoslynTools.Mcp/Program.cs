using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;

var toolRegistry = new ToolRegistry();
var server = new Server(toolRegistry, Console.In, Console.Out);

await server.RunAsync();
