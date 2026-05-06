using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Mcp.Tools;

namespace RoslynTools.Mcp;

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
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<Server>();
        return services;
    }
}
