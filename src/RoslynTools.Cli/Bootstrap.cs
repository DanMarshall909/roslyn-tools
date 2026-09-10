using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoslynTools.Cli.Suggest;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Daemon;
using RoslynTools.Daemon.Rpc;
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
        // Concrete-then-forward registration so both
        // GetService<WorkspaceLoader>() and GetService<IWorkspaceSessionFactory>()
        // return the same disposable singleton. The forwarder, not a second
        // AddSingleton<WorkspaceLoader>, is what keeps disposal correct.
        services.AddSingleton(typeof(ILogger<>), typeof(RtConsoleLogger<>));
        services.AddSingleton<WorkspaceLoader>();
        services.AddSingleton<IWorkspaceSessionFactory>(sp => sp.GetRequiredService<WorkspaceLoader>());
        services.AddSingleton<IDoctorEnvironmentProbe>(_ => new DoctorEnvironmentProbe());
        services.AddSingleton<IDoctorWorkspaceBootstrap, DoctorWorkspaceBootstrap>();
        services.AddSingleton(_ => new RepositoryTrustRegistry());
        services.AddSingleton<TrustAddOperation>();
        services.AddSingleton<TrustListOperation>();
        services.AddSingleton<TrustStatusOperation>();
        services.AddSingleton<TrustRemoveOperation>();
        services.AddSingleton<GitService>();
        services.AddSingleton<DaemonClient>();
        services.AddSingleton<DaemonRpcClient>();
        services.AddSingleton<IDaemonRpcClient>(provider => provider.GetRequiredService<DaemonRpcClient>());
        services.AddSingleton<IDaemonLauncher>(_ => new DaemonLauncher());
        services.AddSingleton<DaemonRpcRecoveryCoordinator>();
        services.AddSingleton(provider => new DaemonCommandDispatcher(
            provider.GetServices<ICommandApplicationPort>(),
            provider.GetRequiredService<RepositoryTrustRegistry>()));
        services.AddSingleton<ISymbolResolutionService, SymbolResolutionService>();
        services.AddSingleton<IDocumentLocatorService, DocumentLocatorService>();
        services.AddSingleton<ISolutionChangeWriter, SolutionChangeWriter>();
        services.AddSingleton<IPreparedFileSystem, PhysicalPreparedFileSystem>();
        services.AddSingleton<IPreparedChangeSetWriter>(provider =>
            new PreparedChangeSetWriter(provider.GetRequiredService<IPreparedFileSystem>()));
        services.AddSingleton<PreparedChangeSetExecutor>();
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
