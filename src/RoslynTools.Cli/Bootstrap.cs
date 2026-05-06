using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Daemon;
using RoslynTools.Services;

namespace RoslynTools;

internal static class Bootstrap
{
    public static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<Core.WorkspaceLoader>();
        services.AddSingleton<GitService>();
        services.AddSingleton<DaemonClient>();
        services.AddSingleton<ISymbolResolutionService, SymbolResolutionService>();
        services.AddSingleton<IDocumentLocatorService, DocumentLocatorService>();
        services.AddSingleton<ISolutionChangeWriter, SolutionChangeWriter>();

        var serviceTypes = typeof(StaticClassInventoryService).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && type.Namespace == "RoslynTools.Services");

        foreach (var serviceType in serviceTypes)
            services.AddTransient(serviceType);
    }
}
