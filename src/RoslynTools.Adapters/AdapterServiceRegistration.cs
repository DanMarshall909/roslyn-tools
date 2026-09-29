using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Services;
using RoslynTools.Services.SemanticDiff;
using RoslynTools.Services.SemanticDiff.Git;

namespace RoslynTools;

/// <summary>Composes third-party integrations outside Core and Hosting.</summary>
internal static class AdapterServiceRegistration
{
    internal static void RegisterCoreApplicationServices(IServiceCollection services)
    {
        services.AddSingleton<ISolutionWriteOutput, ConsoleSolutionWriteOutput>();
        services.AddSingleton<IProductCapabilityCatalog, ProductCapabilityCatalog>();

        // Default source uses this process's directory; explicit request directories
        // obtain a separate client through the same host-owned factory.
        services.AddSingleton<IGitClientFactory, GitClientFactory>();
        services.AddSingleton<IGitClient>(provider => provider.GetRequiredService<IGitClientFactory>()
            .Create(Directory.GetCurrentDirectory()));

        // FileSemanticDiffSourceProvider (an IRtService, auto-scanned below) depends on
        // IFileTextReader for the two-files-on-disc diff mode (US-123). The real reader hits
        // System.IO.File; tests replace it with a fake.
        services.AddSingleton<IFileTextReader, FileTextReader>();

        services.Scan(scan => scan
            .FromAssemblies(typeof(IRtService).Assembly, typeof(AdapterServiceRegistration).Assembly)
            .AddClasses(classes => classes.AssignableTo<IRtService>(), false)
            .AsSelfWithInterfaces()
            .WithTransientLifetime());
    }
}
