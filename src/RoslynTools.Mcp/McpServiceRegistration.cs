using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Mcp.Handlers;
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
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<Server>();

        // Native MCP handlers — return structured JSON instead of routing through
        // CliCommandBridge's text-capture path. Each implements IMcpHandler.
        services.AddSingleton<IMcpHandler, SuggestHandler>();
        services.AddSingleton<IMcpHandler, OutlineHandler>();
        services.AddSingleton<IMcpHandler, FindReferencesHandler>();
        services.AddSingleton<IMcpHandler, RenameSymbolHandler>();
        services.AddSingleton<IMcpHandler, SemanticDiffHandler>();

        return services;
    }
}
