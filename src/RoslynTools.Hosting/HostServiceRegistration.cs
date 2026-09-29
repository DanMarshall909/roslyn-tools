using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using RoslynTools.Configuration;
using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;
using RoslynTools.Services.SemanticDiff;
using RoslynTools.Services.SemanticDiff.Git;
using RoslynTools.Services.Suggest;

namespace RoslynTools;

/// <summary>
/// Registers infrastructure shared by CLI and MCP host adapters.
/// </summary>
internal static class HostServiceRegistration
{
    internal static void RegisterFoundationServices(IServiceCollection services)
    {
        services.TryAddSingleton(HostConfigurationResolution.Default);
        // Concrete-then-forward registration so both
        // GetService<WorkspaceLoader>() and GetService<IWorkspaceSessionFactory>()
        // return the same disposable singleton. The forwarder, not a second
        // AddSingleton<WorkspaceLoader>, is what keeps disposal correct.
        services.AddSingleton(typeof(ILogger<>), typeof(RtConsoleLogger<>));
        services.AddSingleton(sp => new WorkspaceLoader(
            sp.GetRequiredService<ILogger<WorkspaceLoader>>(),
            () => RtContext.ShowWorkspaceWarnings));
        services.AddSingleton<IWorkspaceSessionFactory>(sp => sp.GetRequiredService<WorkspaceLoader>());
        services.AddSingleton<IWorkspaceSnapshotLoader>(sp => sp.GetRequiredService<WorkspaceLoader>());
        services.AddSingleton<IWorkspaceLoadConfiguration>(sp => sp.GetRequiredService<WorkspaceLoader>());
        services.AddSingleton<IWorkspaceEnvironmentIdentity>(_ => new WorkspaceEnvironmentIdentity(() =>
        {
            var identity = MsBuildBootstrap.GetRegisteredSdkIdentity();
            return identity is null ? null : $"{identity.Version}\0{identity.MSBuildPath}";
        }));
        services.AddSingleton<IWorkspaceInputFingerprint>(sp => new WorkspaceInputFingerprint(
            sp.GetRequiredService<IWorkspaceEnvironmentIdentity>(),
            sp.GetRequiredService<IWorkspaceLoadConfiguration>(),
            () => MsBuildBootstrap.GetRegisteredSdkIdentity()?.MSBuildPath));
        services.AddSingleton<IWorkspaceCacheTelemetry>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<WorkspaceCacheTelemetry>>();
            return new WorkspaceCacheTelemetry(
                cacheEvent => logger.RtInfo(
                    "[workspace-cache] {Kind} identity={IdentityHash} revision={SourceRevision} loadCount={LoadCount}",
                    cacheEvent.Kind,
                    cacheEvent.IdentityHash,
                    cacheEvent.SourceRevision,
                    cacheEvent.LoadCount),
                exception => logger.RtWarning(
                    "[workspace-cache] Could not write instrumentation trace: {Message}",
                    exception.Message));
        });
        services.AddSingleton(sp => new ReadOnlyWorkspaceSessionCache(
            sp.GetRequiredService<IWorkspaceSnapshotLoader>(),
            sp.GetRequiredService<IWorkspaceInputFingerprint>(),
            sp.GetRequiredService<IWorkspaceCacheTelemetry>()));
        services.AddSingleton<IReadOnlyWorkspaceSessions>(
            sp => sp.GetRequiredService<IWorkspaceSessionFactory>() is WorkspaceLoader
                ? sp.GetRequiredService<ReadOnlyWorkspaceSessionCache>()
                : sp.GetRequiredService<IWorkspaceSessionFactory>());
        services.AddSingleton<IDoctorEnvironmentProbe>(_ => new DoctorEnvironmentProbe());
        services.AddSingleton(_ => new LocalDurableOperationAuthority());
        services.AddSingleton<IAuthenticatedPrincipalProvider>(provider =>
            provider.GetRequiredService<LocalDurableOperationAuthority>());
        services.AddSingleton<IAuthenticatedOperationContextProvider>(provider =>
            provider.GetRequiredService<LocalDurableOperationAuthority>());
        services.AddSingleton<IChangePlanStoreProtectionAuthority>(provider =>
            provider.GetRequiredService<LocalDurableOperationAuthority>());
        services.AddSingleton<IOperationLedgerProtectionAuthority>(provider =>
            provider.GetRequiredService<LocalDurableOperationAuthority>());
        services.AddSingleton(provider => new DurableChangePlanStore(
            provider.GetRequiredService<LocalDurableOperationAuthority>().PlanStoreRoot,
            protectionAuthority: provider.GetRequiredService<IChangePlanStoreProtectionAuthority>()));
        services.AddSingleton(provider => new DurableOperationLedger(
            provider.GetRequiredService<LocalDurableOperationAuthority>().LedgerRoot,
            protectionAuthority: provider.GetRequiredService<IOperationLedgerProtectionAuthority>()));
        services.AddSingleton(provider => new DurableDispatchIntakeCoordinator(
            provider.GetRequiredService<DurableChangePlanStore>(),
            provider.GetRequiredService<DurableOperationLedger>()));
        services.AddSingleton<IDoctorWorkspaceBootstrap, DoctorWorkspaceBootstrap>();
        services.AddSingleton(_ => new RepositoryTrustRegistry());
        services.AddSingleton<TrustAddOperation>();
        services.AddSingleton<TrustListOperation>();
        services.AddSingleton<TrustStatusOperation>();
        services.AddSingleton<TrustRemoveOperation>();
    }

    internal static void RegisterApplicationInfrastructure(IServiceCollection services)
    {
        services.AddSingleton<DaemonRpcClient>();
        services.AddSingleton<IDaemonRpcClient>(provider => provider.GetRequiredService<DaemonRpcClient>());
        services.AddSingleton<IDaemonLauncher>(provider => new DaemonLauncher(
            DaemonLaunchOptions.FromConfiguration(
                provider.GetRequiredService<HostConfigurationResolution>().Effective.Daemon)));
        services.AddSingleton<DaemonRpcRecoveryCoordinator>();
        services.AddSingleton(provider => new DaemonCommandDispatcher(
            provider.GetServices<ICommandApplicationPort>(),
            provider.GetRequiredService<RepositoryTrustRegistry>(),
            provider.GetService<IDaemonSdkContextGuard>()));
        services.AddSingleton<ISymbolResolutionService, SymbolResolutionService>();
        services.AddSingleton<IDocumentLocatorService, DocumentLocatorService>();
        services.AddSingleton<ParameterChangeHelper>();
        services.AddSingleton<ISolutionWriteOutput, ConsoleSolutionWriteOutput>();
        services.AddSingleton<ISolutionChangeWriter>(provider => new SolutionChangeWriter(
            provider.GetRequiredService<ISolutionWriteOutput>()));
        services.AddSingleton<IPreparedFileSystem, PhysicalPreparedFileSystem>();
        services.AddSingleton<IPreparedChangeSetWriter>(provider =>
            new PreparedChangeSetWriter(provider.GetRequiredService<IPreparedFileSystem>()));
        services.AddSingleton<PreparedChangeSetExecutor>();
    }

    internal static void RegisterSuggestionCatalog(IServiceCollection services)
    {
        services.AddSingleton<ICommandCatalog, SharedSuggestionCatalog>();
    }

    internal static void RegisterApplicationCatalog(IServiceCollection services)
    {
        services.AddSingleton(provider => new ApplicationCommandCatalog(
            () => provider.GetServices<ICommandApplicationPort>()));
    }
}
