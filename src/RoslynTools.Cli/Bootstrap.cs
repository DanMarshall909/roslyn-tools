using Microsoft.Extensions.DependencyInjection;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Daemon;
using RoslynTools.Feedback;
using RoslynTools.Services;
using RoslynTools.Services.GitHub;

namespace RoslynTools;

/// <summary>
/// The bootstrap class for initializing and running the Roslyn tool.
/// </summary>
internal static class Bootstrap
{
    public static void RegisterServices(IServiceCollection services)
    {
        HostServiceRegistration.RegisterFoundationServices(services);
        services.AddSingleton<Core.Decisions.IDecisionFileStore, Adapters.Decisions.YamlDecisionFileStore>();
        services.AddSingleton<DaemonClient>();
        HostServiceRegistration.RegisterApplicationInfrastructure(services);
        HostServiceRegistration.RegisterSuggestionCatalog(services);
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
        HostServiceRegistration.RegisterApplicationCatalog(services);
        services.AddSingleton<CommandDescriptorRegistry>();
        HostServiceRegistration.RegisterCoreApplicationServices(services);

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
