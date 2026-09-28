using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Daemon;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Architecture;

public sealed class HostAssemblyBoundaryTests
{
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
