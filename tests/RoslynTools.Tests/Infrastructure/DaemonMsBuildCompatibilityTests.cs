using RoslynTools.Core;
using RoslynTools.Daemon;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class DaemonMsBuildCompatibilityTests
{
    [Fact]
    public void same_msbuild_identity_is_compatible_across_distinct_worktree_paths()
    {
        var identity = Identity("sdk-a", new Version(10, 0, 201));
        var compatibility = new DaemonMsBuildCompatibility(
            identity,
            _ => identity);

        compatibility.Check("/repo-main").Unwrap().ShouldBeTrue();
        compatibility.Check("/repo-linked-worktree").Unwrap().ShouldBeTrue();
    }

    [Fact]
    public void msbuild_path_case_uses_the_operating_system_path_rule()
    {
        var registered = Identity("sdk-case", new Version(10, 0, 201));
        var requested = MsBuildInstanceIdentity.Create(
            registered.MSBuildPath.ToUpperInvariant(),
            registered.Version);
        var compatibility = new DaemonMsBuildCompatibility(
            registered,
            _ => requested);

        compatibility.Check("/repo").IsOk.ShouldBe(OperatingSystem.IsWindows());
    }

    [Fact]
    public void same_version_at_a_different_msbuild_path_requires_a_daemon_restart()
    {
        var registered = Identity("sdk-a", new Version(10, 0, 201));
        var requested = Identity("sdk-b", new Version(10, 0, 201));
        var compatibility = new DaemonMsBuildCompatibility(
            registered,
            _ => requested);

        var result = compatibility.Check("/repo");

        result.IsErr.ShouldBeTrue();
        var error = result.Match(
            _ => throw new InvalidOperationException("Expected SDK incompatibility."),
            value => value);
        error.Kind.ShouldBe(RtErrorKind.ExternalToolFailure);
        error.Message.ShouldContain("10.0.201");
        error.Message.ShouldContain("restart", Case.Insensitive);
        error.Message.ShouldContain(registered.MSBuildPath);
        error.Message.ShouldContain(requested.MSBuildPath);
        error.Context.ShouldNotBeNull();
        error.Context["daemonRestartRequired"].ShouldBe("true");
        error.Context["registeredSdkVersion"].ShouldBe("10.0.201");
        error.Context["registeredMsBuildPath"].ShouldBe(registered.MSBuildPath);
        error.Context["requestedSdkVersion"].ShouldBe("10.0.201");
        error.Context["requestedMsBuildPath"].ShouldBe(requested.MSBuildPath);
        error.Context["requestedDirectory"].ShouldBe("/repo");
    }

    [Fact]
    public void unavailable_sdk_selection_fails_closed_with_restart_context()
    {
        var compatibility = new DaemonMsBuildCompatibility(
            Identity("sdk-a", new Version(10, 0, 201)),
            _ => null);

        var result = compatibility.Check("/repo");

        result.IsErr.ShouldBeTrue();
        var error = result.Match(
            _ => throw new InvalidOperationException("Expected SDK selection failure."),
            value => value);
        error.Kind.ShouldBe(RtErrorKind.ExternalToolFailure);
        error.Message.ShouldContain("No compatible .NET SDK");
        error.Context.ShouldNotBeNull();
        error.Context["daemonRestartRequired"].ShouldBe("true");
        error.Context["requestedDirectory"].ShouldBe("/repo");
    }

    [Fact]
    public void default_resolver_accepts_the_sdk_identity_selected_for_the_same_directory()
    {
        var repositoryRoot = FindRepoRoot();
        var instance = RoslynTools.MsBuildBootstrap.FindPreferredInstance(repositoryRoot);
        instance.ShouldNotBeNull();
        var compatibility = new DaemonMsBuildCompatibility(
            MsBuildInstanceIdentity.From(instance));

        compatibility.Check(repositoryRoot).Unwrap().ShouldBeTrue();
    }

    [Fact]
    public void missing_sdk_instance_maps_to_no_identity()
    {
        DaemonMsBuildCompatibility.CreateIdentity(instance: null).ShouldBeNull();
    }

    [Fact]
    public void sdk_selection_exception_fails_closed_with_restart_context()
    {
        var compatibility = new DaemonMsBuildCompatibility(
            Identity("sdk-a", new Version(10, 0, 201)),
            _ => throw new InvalidOperationException("broken SDK selection"));

        var result = compatibility.Check("/repo");

        result.IsErr.ShouldBeTrue();
        var error = result.Match(
            _ => throw new InvalidOperationException("Expected SDK selection failure."),
            value => value);
        error.Kind.ShouldBe(RtErrorKind.ExternalToolFailure);
        error.Message.ShouldContain("broken SDK selection");
        error.Context.ShouldNotBeNull();
        error.Context["daemonRestartRequired"].ShouldBe("true");
        error.Context["requestedDirectory"].ShouldBe("/repo");
    }

    private static MsBuildInstanceIdentity Identity(string directoryName, Version version) =>
        MsBuildInstanceIdentity.Create(
            Path.Combine(Path.GetTempPath(), "rt-daemon-sdk-tests", directoryName),
            version);

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RoslynTools.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate RoslynTools.slnx.");
    }
}
