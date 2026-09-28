using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;

namespace RoslynTools.Daemon;

/// <summary>Composes application ports for the daemon host without CLI services.</summary>
internal static class DaemonHostServiceRegistration
{
    internal static void RegisterServices(
        IServiceCollection services,
        MsBuildInstanceIdentity registeredIdentity)
    {
        HostServiceRegistration.RegisterFoundationServices(services);
        HostServiceRegistration.RegisterApplicationInfrastructure(services);
        HostServiceRegistration.RegisterSuggestionCatalog(services);
        HostServiceRegistration.RegisterApplicationCatalog(services);
        HostServiceRegistration.RegisterCoreApplicationServices(services);
        services.AddSingleton<IDaemonSdkContextGuard>(
            new DaemonMsBuildCompatibility(registeredIdentity));
    }
}
