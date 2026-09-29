using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Cli.Suggest;
using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Daemon;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp;
using RoslynTools.Services;
using RoslynTools.Services.Suggest;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Architecture;

public sealed class HostAssemblyBoundaryTests
{
    [Fact]
    public void shared_suggestion_catalogue_matches_cli_command_metadata()
    {
        var services = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(services);
        using var provider = services.BuildServiceProvider();

        var shared = new SharedSuggestionCatalog(
            provider.GetRequiredService<ApplicationCommandCatalog>());
        var cli = new AppCommandCatalog();

        typeof(SharedSuggestionCatalog).Assembly.ShouldBe(typeof(MsBuildBootstrap).Assembly);
        provider.GetRequiredService<ICommandCatalog>().ShouldBeOfType<SharedSuggestionCatalog>();
        shared.All.Count.ShouldBe(cli.All.Count);
        var cliByName = cli.All.ToDictionary(info => info.Name, StringComparer.Ordinal);
        shared.All.Select(info => info.Name).Order(StringComparer.Ordinal)
            .ShouldBe(cliByName.Keys.Order(StringComparer.Ordinal));
        var metadataMismatches = shared.All
            .Where(info => JsonSerializer.Serialize(info)
                           != JsonSerializer.Serialize(cliByName[info.Name]))
            .Select(info => info.Name)
            .ToArray();
        metadataMismatches.ShouldBeEmpty();

        foreach (var query in new[]
                 {
                     "rename a symbol",
                     "trust a repository",
                     "show effective configuration",
                     "record command feedback",
                     "extract method"
                 })
        {
            var request = new SuggestRequest(query, Top: 5);
            JsonSerializer.Serialize(new SuggestService(shared).Suggest(request))
                .ShouldBe(JsonSerializer.Serialize(new SuggestService(cli).Suggest(request)));
        }
    }

    [Fact]
    public void local_host_command_metadata_has_one_shared_source()
    {
        typeof(HostCommandMetadata).Assembly.ShouldBe(typeof(MsBuildBootstrap).Assembly);
        ConfigCommand.Info.ShouldBeSameAs(HostCommandMetadata.Config);
        TrustCommand.Info.ShouldBeSameAs(HostCommandMetadata.Trust);
        SemanticDiffCommand.Info.ShouldBeSameAs(HostCommandMetadata.SemanticDiff);
        new AppCommandCatalog().All.Single(info => info.Name == "feedback")
            .ShouldBeSameAs(HostCommandMetadata.Feedback);
    }

    [Fact]
    public void shared_application_composition_registers_catalogue_and_core_operations()
    {
        var services = new ServiceCollection();

        HostServiceRegistration.RegisterApplicationCatalog(services);
        AdapterServiceRegistration.RegisterCoreApplicationServices(services);

        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(ApplicationCommandCatalog));
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(IProductCapabilityCatalog));
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(SuggestOperation));
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(ICommandApplicationPort));
        services.ShouldNotContain(descriptor => descriptor.ServiceType.Assembly.GetName().Name == "rt");
    }

    [Fact]
    public void shared_application_infrastructure_registers_daemon_and_change_services()
    {
        var services = new ServiceCollection();

        HostServiceRegistration.RegisterApplicationInfrastructure(services);

        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(IDaemonRpcClient));
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(IDaemonLauncher));
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(DaemonCommandDispatcher));
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(ISymbolResolutionService));
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(IDocumentLocatorService));
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(IPreparedChangeSetWriter));
    }

    [Fact]
    public void shared_host_foundation_registers_workspace_durable_trust_and_doctor_ports()
    {
        var services = new ServiceCollection();

        HostServiceRegistration.RegisterFoundationServices(services);

        typeof(HostServiceRegistration).Assembly.ShouldBe(typeof(MsBuildBootstrap).Assembly);
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(IWorkspaceSessionFactory));
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(DurableChangePlanStore));
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(DurableOperationLedger));
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(RepositoryTrustRegistry));
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(IDoctorEnvironmentProbe));
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(IDoctorWorkspaceBootstrap));
    }

    [Fact]
    public void doctor_host_probe_and_workspace_bootstrap_live_in_shared_host_assembly()
    {
        var hostingAssembly = typeof(MsBuildBootstrap).Assembly;

        typeof(DoctorEnvironmentProbe).Assembly.ShouldBe(hostingAssembly);
        typeof(DoctorWorkspaceBootstrap).Assembly.ShouldBe(hostingAssembly);
        typeof(DoctorSdkSelection).Assembly.ShouldBe(hostingAssembly);
    }

    [Fact]
    public void process_global_msbuild_bootstrap_lives_in_shared_host_assembly()
    {
        var hostingAssembly = typeof(MsBuildBootstrap).Assembly;
        hostingAssembly.GetName().Name.ShouldBe("RoslynTools.Hosting");
        typeof(DoctorSdkRequest).Assembly.ShouldBe(hostingAssembly);

        var references = hostingAssembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();
        references.ShouldContain("RoslynTools");
        references.ShouldNotContain("rt");
        references.ShouldNotContain("RoslynTools.Mcp");
    }

    [Fact]
    public void daemon_host_owns_its_application_composition_without_a_cli_dependency()
    {
        var daemonAssembly = typeof(DaemonServer).Assembly;
        daemonAssembly.GetName().Name.ShouldBe("RoslynTools.DaemonHost");
        var references = daemonAssembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();
        references.ShouldContain("RoslynTools");
        references.ShouldContain("RoslynTools.Hosting");
        references.ShouldNotContain("rt");
        references.ShouldNotContain("RoslynTools.Mcp");
        references.ShouldNotContain("System.CommandLine");

        var identity = MsBuildInstanceIdentity.Create(
            Path.Combine(Path.GetTempPath(), "daemon-host-sdk"),
            new Version(10, 0, 100));
        var daemonServices = new ServiceCollection();
        DaemonHostServiceRegistration.RegisterServices(daemonServices, identity);
        daemonServices.ShouldContain(descriptor => descriptor.ServiceType == typeof(IDaemonSdkContextGuard));
        daemonServices.ShouldContain(descriptor => descriptor.ServiceType == typeof(DaemonCommandDispatcher));

        using var daemonProvider = daemonServices.BuildServiceProvider();
        using var mcpProvider = new ServiceCollection()
            .AddMcpHostServices(Stream.Null, Stream.Null)
            .BuildServiceProvider();
        daemonProvider.GetRequiredService<ApplicationCommandCatalog>().All
            .Select(descriptor => descriptor.ToolName)
            .Order(StringComparer.Ordinal)
            .ShouldBe(mcpProvider.GetRequiredService<ApplicationCommandCatalog>().All
                .Select(descriptor => descriptor.ToolName)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task daemon_rpc_resolves_shared_ports_but_not_cli_only_feedback()
    {
        var cliServices = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(cliServices);
        using var cliProvider = cliServices.BuildServiceProvider();
        cliProvider.GetServices<ICommandApplicationPort>()
            .ShouldContain(port => port.Definition.Name == "feedback_list");

        var identity = MsBuildInstanceIdentity.Create(
            Path.Combine(Path.GetTempPath(), "daemon-host-sdk"),
            new Version(10, 0, 100));
        var daemonServices = new ServiceCollection();
        DaemonHostServiceRegistration.RegisterServices(daemonServices, identity);
        using var daemonProvider = daemonServices.BuildServiceProvider();
        var dispatcher = daemonProvider.GetRequiredService<DaemonCommandDispatcher>();

        using var invalidFindPattern = JsonDocument.Parse(
            """{"file":123,"startLine":0,"endLine":1}""");
        var typed = await dispatcher.ExecuteAsync(new DaemonRpcRequest(
            DaemonRpcProtocol.CurrentVersion,
            "typed-port",
            DaemonRpcProtocol.ExecuteCommandMethod,
            FindPatternCommandDefinition.Name,
            invalidFindPattern.RootElement.Clone()));
        typed.Error.ShouldNotBeNull();
        typed.Error.Code.ShouldBe(DaemonRpcErrorCodes.InvalidArguments);

        using var empty = JsonDocument.Parse("{}");
        var feedback = await dispatcher.ExecuteAsync(new DaemonRpcRequest(
            DaemonRpcProtocol.CurrentVersion,
            "cli-only-feedback",
            DaemonRpcProtocol.ExecuteCommandMethod,
            "feedback_list",
            empty.RootElement.Clone()));
        feedback.Error.ShouldNotBeNull();
        feedback.Error.Code.ShouldBe(DaemonRpcErrorCodes.CommandNotFound);
    }

    [Fact]
    public void shared_msbuild_identity_and_compatibility_live_in_the_reusable_assembly()
    {
        var reusableAssembly = typeof(ApplicationCommandCatalog).Assembly;

        typeof(MsBuildInstanceIdentity).Assembly.ShouldBe(reusableAssembly);
        typeof(MsBuildInstanceSelection).Assembly.ShouldBe(reusableAssembly);
        typeof(MsBuildInstanceCompatibility).Assembly.ShouldBe(reusableAssembly);
    }
}
