using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Daemon;
using RoslynTools.Services;

namespace RoslynTools;

/// <summary>
/// The bootstrap class for initializing and running the Roslyn tool.
/// </summary>
internal static class Bootstrap
{
    public static void RegisterServices(IServiceCollection services)
    {
        // Concrete-then-forward registration so both
        // GetService<WorkspaceLoader>() and GetService<IWorkspaceSessionFactory>()
        // return the same disposable singleton. The forwarder, not a second
        // AddSingleton<WorkspaceLoader>, is what keeps disposal correct.
        services.AddSingleton<WorkspaceLoader>();
        services.AddSingleton<IWorkspaceSessionFactory>(sp => sp.GetRequiredService<WorkspaceLoader>());
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
