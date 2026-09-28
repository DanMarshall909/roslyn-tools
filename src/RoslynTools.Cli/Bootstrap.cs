using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Cli.Suggest;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Daemon;
using RoslynTools.Feedback;
using RoslynTools.Services;
using RoslynTools.Services.GitHub;
using RoslynTools.Services.SemanticDiff;
using RoslynTools.Services.SemanticDiff.Git;
using RoslynTools.Services.Suggest;

namespace RoslynTools;

/// <summary>
/// The bootstrap class for initializing and running the Roslyn tool.
/// </summary>
internal static class Bootstrap
{
    public static void RegisterServices(IServiceCollection services)
    {
        HostServiceRegistration.RegisterFoundationServices(services);
        services.AddSingleton<GitService>();
        services.AddSingleton<DaemonClient>();
        HostServiceRegistration.RegisterApplicationInfrastructure(services);
        services.AddSingleton<ICommandCatalog, AppCommandCatalog>();
        services.AddSingleton<FeedbackAddOperation>();
        services.AddSingleton<FeedbackListOperation>();
        services.AddSingleton<FeedbackProcessOperation>();
        services.AddSingleton<IGitHubIssueClient, GitHubIssueClient>();
        services.AddSingleton<FeedbackReportOperation>();
        services.AddSingleton<ICommandApplicationPort>(
            sp => sp.GetRequiredService<FeedbackAddOperation>());
        services.AddSingleton<ICommandApplicationPort>(
            sp => sp.GetRequiredService<FeedbackListOperation>());
        services.AddSingleton<ICommandApplicationPort>(
            sp => sp.GetRequiredService<FeedbackProcessOperation>());
        services.AddSingleton<ICommandApplicationPort>(
            sp => sp.GetRequiredService<FeedbackReportOperation>());
        services.AddSingleton(provider => new ApplicationCommandCatalog(
            () => provider.GetServices<ICommandApplicationPort>()));
        services.AddSingleton<CommandDescriptorRegistry>();
        services.AddSingleton<IProductCapabilityCatalog, ProductCapabilityCatalog>();

        // GitSemanticDiffSourceProvider (an IRtService, so auto-scanned below) depends on
        // IGitClient. The git working directory is the process's current directory; the
        // batch-5 command can override per-invocation if it ever needs a different root.
        services.AddSingleton<IGitClient>(_ => new GitClient(Directory.GetCurrentDirectory()));

        // FileSemanticDiffSourceProvider (an IRtService, auto-scanned below) depends on
        // IFileTextReader for the two-files-on-disc diff mode (US-123). The real reader hits
        // System.IO.File; tests replace it with a fake.
        services.AddSingleton<IFileTextReader, FileTextReader>();

        services.Scan(scan => scan
            .FromAssemblyOf<IRtService>()
            .AddClasses(classes => classes.AssignableTo<IRtService>(), false)
            .AsSelfWithInterfaces()
            .WithTransientLifetime());

        // Register every IRtCommand in this assembly as a transient so commands that move
        // to ctor-injection can be resolved via App.Discover (US-072). The set is small
        // enough (one instance per CLI invocation) that transient is the right lifetime.
        // Non-generic AssignableTo because IRtCommand has static-abstract members.
        services.Scan(scan => scan
            .FromAssemblies(typeof(Bootstrap).Assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(IRtCommand)), false)
            .AsSelf()
            .WithTransientLifetime());
    }
}
