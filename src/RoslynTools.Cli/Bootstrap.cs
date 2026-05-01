using Microsoft.Extensions.DependencyInjection;

namespace RoslynTools;

/// <summary>
/// Service registration for the DI host introduced in US-022.
/// Commands continue to use static services for now; this scaffold
/// provides the registration target for the static-to-instance migration
/// in subsequent stories.
/// </summary>
internal static class Bootstrap
{
    /// <summary>
    /// Registers application services into the DI container.
    /// Currently empty: commands still call static services directly.
    /// Subsequent stories (US-023+) will move registrations here.
    /// </summary>
    public static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<Services.StaticClassInventoryService>();
    }
}
