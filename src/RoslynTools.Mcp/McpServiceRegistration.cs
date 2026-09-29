using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RoslynTools.Core;
using RoslynTools.Mcp.Tools;

namespace RoslynTools.Mcp;

/// <summary>
/// Class for registering MCP services.
/// </summary>
internal static class McpServiceRegistration
{
    public static IServiceCollection AddMcpHostServices(
        this IServiceCollection services,
        TextReader input,
        TextWriter output,
        McpHostOptions? hostOptions = null)
    {
        HostServiceRegistration.RegisterFoundationServices(services);
        HostServiceRegistration.RegisterApplicationInfrastructure(services);
        HostServiceRegistration.RegisterSuggestionCatalog(services);
        HostServiceRegistration.RegisterApplicationCatalog(services);
        AdapterServiceRegistration.RegisterCoreApplicationServices(services);
        services.Replace(ServiceDescriptor.Singleton<ISolutionWriteOutput>(NullSolutionWriteOutput.Instance));
        return services.AddMcpServices(input, output, hostOptions);
    }

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
        services.AddSingleton<IMcpMsBuildBinding, McpMsBuildBinding>();
        services.AddSingleton<McpCommandAdapter>();
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<Server>();

        return services;
    }
}
