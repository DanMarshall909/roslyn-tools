using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Daemon;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Architecture;

public sealed class HostAssemblyBoundaryTests
{
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
    public void shared_msbuild_identity_and_compatibility_live_in_the_reusable_assembly()
    {
        var reusableAssembly = typeof(ApplicationCommandCatalog).Assembly;

        typeof(MsBuildInstanceIdentity).Assembly.ShouldBe(reusableAssembly);
        typeof(MsBuildInstanceSelection).Assembly.ShouldBe(reusableAssembly);
        typeof(MsBuildInstanceCompatibility).Assembly.ShouldBe(reusableAssembly);
    }
}
