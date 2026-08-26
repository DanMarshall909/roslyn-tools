using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp.Daemon;
using RoslynTools.Mcp.Tools;

namespace RoslynTools.Mcp;

/// <summary>
/// Class for registering MCP services.
/// </summary>
internal static class McpServiceRegistration
{
    public static IServiceCollection AddMcpServices(
        this IServiceCollection services,
        TextReader input,
        TextWriter output)
    {
        services.AddSingleton(input);
        services.AddSingleton(output);
        services.AddSingleton<ToolSchemaBuilder>();
        services.AddSingleton<CliCommandBridge>();
        services.AddSingleton<IDaemonRpcClient>(
            provider => provider.GetRequiredService<DaemonRpcClient>());
        services.AddSingleton<IMcpDaemonLauncher>(_ => new McpDaemonLauncher());
        services.AddSingleton<DaemonRpcRecovery>();
        services.AddSingleton<McpCommandAdapter>();
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<Server>();

        return services;
    }
}
