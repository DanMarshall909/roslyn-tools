using Microsoft.Build.Locator;

namespace RoslynTools.Tests.Infrastructure;

public class MsBuildBootstrap
{
    [Fact]
    public void finds_preferred_instance_returns_dot_net_sdk_when_available()
    {
        var instance = RoslynTools.MsBuildBootstrap.FindPreferredInstance();

        // On any machine with a .NET SDK installed (CI included), we expect
        // a DotNetSdk instance rather than a VS/.NET Framework one.
        Assert.NotNull(instance);
        Assert.Equal(DiscoveryType.DotNetSdk, instance.DiscoveryType);
    }

    [Fact]
    public void finds_preferred_instance_returns_highest_version_first()
    {
        var instance = RoslynTools.MsBuildBootstrap.FindPreferredInstance();
        if (instance == null) return; // no SDK installed, skip

        var allSdk = MSBuildLocator.QueryVisualStudioInstances()
            .Where(i => i.DiscoveryType == DiscoveryType.DotNetSdk)
            .ToList();

        var highest = allSdk.Max(i => i.Version);
        Assert.Equal(highest, instance.Version);
    }
}
