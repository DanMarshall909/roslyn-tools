using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Configuration;
using RoslynTools.Daemon.Rpc;
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
        TextWriter output,
        McpHostOptions? hostOptions = null)
    {
        services.AddSingleton(input);
        services.AddSingleton(output);
        services.AddSingleton(hostOptions ?? new McpHostOptions());
        services.AddSingleton<ToolSchemaBuilder>();
        services.AddSingleton<IDaemonRpcClient>(
            provider => provider.GetRequiredService<DaemonRpcClient>());
        services.AddSingleton<IDaemonLauncher>(provider => new DaemonLauncher(
            DaemonLaunchOptions.FromConfiguration(
                provider.GetRequiredService<HostConfigurationResolution>().Effective.Daemon)));
        services.AddSingleton<DaemonRpcRecoveryCoordinator>();
        services.AddSingleton<IMcpMsBuildBinding, McpMsBuildBinding>();
        services.AddSingleton<McpCommandAdapter>();
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<Server>();

        return services;
    }
}
