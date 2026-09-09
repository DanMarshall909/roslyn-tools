using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests;

public sealed class RepositoryTrustPolicyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"rt-trust-policy-{Guid.NewGuid():N}");

    public RepositoryTrustPolicyTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void default_cli_and_ci_do_not_read_or_create_the_registry()
    {
        var store = CorruptStore();

        using (RepositoryTrustPolicy.EnterCliScope(false, null, false, store))
            RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();

        File.ReadAllText(store.FilePath).ShouldBe("invalid JSON");
        File.Exists(store.FilePath + ".lock").ShouldBeFalse();
    }

    [Fact]
    public void explicit_one_shot_approval_wins_without_reading_saved_state()
    {
        var store = CorruptStore();

        using (RepositoryTrustPolicy.EnterCliScope(true, _directory, true, store))
            RepositoryTrustContext.IsExplicitlyTrustedFor(Path.Combine(_directory, "App.slnx")).ShouldBeTrue();

        RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
    }

    [Fact]
    public void saved_grants_are_opt_in_and_removal_applies_to_the_next_invocation()
    {
        var store = new RepositoryTrustStore(Path.Combine(_directory, "trusted-roots.json"));
        store.Add(_directory);

        using (RepositoryTrustPolicy.EnterCliScope(false, null, false, store))
            RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
        using (RepositoryTrustPolicy.EnterCliScope(false, null, true, store))
            RepositoryTrustContext.IsExplicitlyTrustedFor(_directory).ShouldBeTrue();

        store.Remove(_directory);
        using (RepositoryTrustPolicy.EnterCliScope(false, null, true, store))
            RepositoryTrustContext.IsExplicitlyTrustedFor(_directory).ShouldBeFalse();
    }

    [Fact]
    public void corrupted_opted_in_registry_fails_without_replacing_outer_authority()
    {
        var store = CorruptStore();
        using (RepositoryTrustContext.UseExplicitTrust(_directory))
        {
            Should.Throw<InvalidDataException>(() => RepositoryTrustPolicy.EnterCliScope(false, null, true, store));
            RepositoryTrustContext.IsExplicitlyTrustedFor(_directory).ShouldBeTrue();
        }
    }

    [Fact]
    public void missing_saved_directory_fails_closed()
    {
        var root = Directory.CreateDirectory(Path.Combine(_directory, "repository")).FullName;
        var store = new RepositoryTrustStore(Path.Combine(_directory, "trusted-roots.json"));
        store.Add(root);
        Directory.Delete(root);

        Should.Throw<ArgumentException>(() => RepositoryTrustPolicy.ReadUsableRoots(store));
        RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
    }

    [Fact]
    public void legacy_cli_bridge_preserves_its_host_owned_scope()
    {
        var store = CorruptStore();
        using (RepositoryTrustContext.UseExplicitTrust(_directory))
        using (RepositoryTrustPolicy.EnterCliScope(false, null, false, store))
            RepositoryTrustContext.IsExplicitlyTrustedFor(_directory).ShouldBeTrue();
    }

    [SkippableFact]
    public void approval_does_not_follow_a_linked_descendant_outside_its_root()
    {
        var root = Directory.CreateDirectory(Path.Combine(_directory, "approved")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(_directory, "outside")).FullName;
        var link = Path.Combine(root, "redirected");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Skip.If(true, $"Creating a symlink is unavailable: {exception.Message}");
        }

        using (RepositoryTrustContext.UseExplicitTrust(root))
        {
            RepositoryTrustContext.IsExplicitlyTrustedFor(Path.Combine(root, "App.slnx")).ShouldBeTrue();
            RepositoryTrustContext.IsExplicitlyTrustedFor(Path.Combine(link, "App.slnx")).ShouldBeFalse();
        }
    }

    private RepositoryTrustStore CorruptStore()
    {
        var path = Path.Combine(_directory, "trusted-roots.json");
        File.WriteAllText(path, "invalid JSON");
        return new RepositoryTrustStore(path);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
