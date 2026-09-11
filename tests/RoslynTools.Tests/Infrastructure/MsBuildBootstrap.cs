using Microsoft.Build.Locator;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public class MsBuildBootstrap
{
    [Theory]
    [InlineData(nameof(DoctorSdkRequestState.Absent), false, nameof(MsBuildRegistrationPath.UnscopedFallback))]
    [InlineData(nameof(DoctorSdkRequestState.Valid), false, nameof(MsBuildRegistrationPath.RequestedSdkUnavailable))]
    [InlineData(nameof(DoctorSdkRequestState.Invalid), false, nameof(MsBuildRegistrationPath.RequestedSdkUnavailable))]
    [InlineData(nameof(DoctorSdkRequestState.Absent), true, nameof(MsBuildRegistrationPath.ScopedInstance))]
    [InlineData(nameof(DoctorSdkRequestState.Valid), true, nameof(MsBuildRegistrationPath.ScopedInstance))]
    public void unscoped_sdk_fallback_is_forbidden_when_global_json_is_authoritative(
        string requestState,
        bool scopedInstanceFound,
        string expected)
    {
        RoslynTools.MsBuildBootstrap.SelectRegistrationPath(
                Enum.Parse<DoctorSdkRequestState>(requestState),
                scopedInstanceFound)
            .ShouldBe(Enum.Parse<MsBuildRegistrationPath>(expected));
    }

    [Fact]
    public void finds_preferred_instance_returns_dot_net_sdk_when_available()
    {
        var instance = RoslynTools.MsBuildBootstrap.FindPreferredInstance();

        // On any machine with a .NET SDK installed (CI included), we expect
        // a DotNetSdk instance rather than a VS/.NET Framework one.
        instance.ShouldNotBeNull();
        instance.DiscoveryType.ShouldBe(DiscoveryType.DotNetSdk);
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
        instance.Version.ShouldBe(highest);
    }
}
