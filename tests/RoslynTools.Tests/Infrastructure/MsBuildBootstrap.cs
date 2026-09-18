using Microsoft.Build.Locator;
using RoslynTools.Daemon;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public class MsBuildBootstrap
{
    [Fact]
    public void versionless_sdk_object_is_valid_and_can_reach_scoped_resolution()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"rt-versionless-sdk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var globalJsonPath = Path.Combine(directory, "global.json");
            File.WriteAllText(globalJsonPath, """
                {"sdk":{"allowPrerelease":true,"paths":[]}}
                """);

            var request = RoslynTools.MsBuildBootstrap.InspectSdkRequest(directory);

            request.State.ShouldBe(DoctorSdkRequestState.Valid);
            request.GlobalJsonPath.ShouldBe(globalJsonPath);
            request.RequestedVersion.ShouldBeNull();
            request.Diagnostic.ShouldBeNull();
            RoslynTools.MsBuildBootstrap.SelectRegistrationPath(request.State, scopedInstanceFound: true)
                .ShouldBe(MsBuildRegistrationPath.ScopedInstance);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void versioned_sdk_object_preserves_the_requested_version()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"rt-versioned-sdk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "global.json"), """
                {"sdk":{"version":"10.0.201","allowPrerelease":true}}
                """);

            var request = RoslynTools.MsBuildBootstrap.InspectSdkRequest(directory);

            request.State.ShouldBe(DoctorSdkRequestState.Valid);
            request.RequestedVersion.ShouldBe("10.0.201");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void whitespace_sdk_version_is_invalid()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"rt-whitespace-sdk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "global.json"), """
                {"sdk":{"version":"   "}}
                """);

            var request = RoslynTools.MsBuildBootstrap.InspectSdkRequest(directory);

            request.State.ShouldBe(DoctorSdkRequestState.Invalid);
            request.RequestedVersion.ShouldBeNull();
            request.Diagnostic.ShouldNotBeNull();
            request.Diagnostic!.ShouldContain("non-empty string");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

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

    [Fact]
    public void Native_locator_failure_uses_the_exact_dotnet_selected_sdk_fallback()
    {
        var expected = new MsBuildInstanceSelection(
            MsBuildInstanceIdentity.Create(Path.GetTempPath(), new Version(10, 0, 303)),
            () => { });

        var selected = RoslynTools.MsBuildBootstrap.SelectPreferredSelection(
            () => throw new DllNotFoundException("hostfxr"),
            () => expected);

        selected.ShouldBeSameAs(expected);
    }

    [Fact]
    public void Successful_native_locator_does_not_run_the_manual_fallback()
    {
        var expected = new MsBuildInstanceSelection(
            MsBuildInstanceIdentity.Create(Path.GetTempPath(), new Version(10, 0, 303)),
            () => { });
        var fallbackCalled = false;

        var selected = RoslynTools.MsBuildBootstrap.SelectPreferredSelection(
            () => expected,
            () =>
            {
                fallbackCalled = true;
                return null;
            });

        selected.ShouldBeSameAs(expected);
        fallbackCalled.ShouldBeFalse();
    }
}
