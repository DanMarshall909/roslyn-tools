using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Daemon;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Architecture;

public sealed class HostAssemblyBoundaryTests
{
    [Fact]
    public void shared_msbuild_identity_and_compatibility_live_in_the_reusable_assembly()
    {
        var reusableAssembly = typeof(ApplicationCommandCatalog).Assembly;

        typeof(MsBuildInstanceIdentity).Assembly.ShouldBe(reusableAssembly);
        typeof(MsBuildInstanceSelection).Assembly.ShouldBe(reusableAssembly);
        typeof(MsBuildInstanceCompatibility).Assembly.ShouldBe(reusableAssembly);
    }
}
