using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Daemon;

namespace RoslynTools;

internal static class Bootstrap
{
    public static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<Core.WorkspaceLoader>();
        services.AddSingleton<Services.StaticClassInventoryService>();
        services.AddSingleton<GitService>();
        services.AddSingleton<DaemonClient>();
    }
}
