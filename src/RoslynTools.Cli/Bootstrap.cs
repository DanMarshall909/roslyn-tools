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
        services.Scan(scan => scan
            .FromAssemblyOf<IRtService>()
            .AddClasses(classes => classes.AssignableTo<IRtService>(), publicOnly: false)
            .AsSelfWithInterfaces()
            .WithTransientLifetime());
    }
}
