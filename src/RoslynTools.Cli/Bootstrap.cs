using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Daemon;
using RoslynTools.Services;

namespace RoslynTools;

internal static class Bootstrap
{
    public static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<WorkspaceLoader>();
        services.AddSingleton<GitService>();
        services.AddSingleton<DaemonClient>();
        services.AddSingleton<ISymbolResolutionService, SymbolResolutionService>();
        services.AddSingleton<IDocumentLocatorService, DocumentLocatorService>();
        services.AddSingleton<ISolutionChangeWriter, SolutionChangeWriter>();
        services.Scan(scan => scan
            .FromAssemblyOf<IRtService>()
            .AddClasses(classes => classes.AssignableTo<IRtService>(), false)
            .AsSelfWithInterfaces()
            .WithTransientLifetime());
    }
}
