using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class RepositoryTrustContextTests
{
    [Fact]
    public void repository_bound_trust_authorizes_only_paths_beneath_the_selected_root()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"rt-trust-{Guid.NewGuid():N}");
        var repositoryRoot = Path.Combine(parent, "repo");
        var solutionPath = Path.Combine(repositoryRoot, "src", "App.slnx");
        var siblingSolutionPath = Path.Combine(parent, "repo-copy", "Other.slnx");

        using (RepositoryTrustContext.UseExplicitTrust(repositoryRoot))
        {
            RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeTrue();
            RepositoryTrustContext.IsExplicitlyTrustedFor(solutionPath).ShouldBeTrue();
            RepositoryTrustContext.IsExplicitlyTrustedFor(siblingSolutionPath).ShouldBeFalse();
        }

        RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
    }

    [Fact]
    public void cli_invocation_trust_remains_unrestricted_within_its_existing_scope()
    {
        var solutionPath = Path.Combine(Path.GetTempPath(), "any-repository", "App.slnx");

        using (RepositoryTrustContext.UseExplicitTrust())
            RepositoryTrustContext.IsExplicitlyTrustedFor(solutionPath).ShouldBeTrue();

        RepositoryTrustContext.IsExplicitlyTrustedFor(solutionPath).ShouldBeFalse();
    }

    [Fact]
    public void multiple_repository_roots_authorize_only_descendant_paths_of_each_root()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"rt-trust-{Guid.NewGuid():N}");
        var firstRoot = Path.Combine(parent, "first");
        var secondRoot = Path.Combine(parent, "second");
        var untrustedRoot = Path.Combine(parent, "untrusted");

        using (RepositoryTrustContext.UseExplicitTrust([firstRoot, secondRoot]))
        {
            RepositoryTrustContext.IsExplicitlyTrustedFor(Path.Combine(firstRoot, "App.slnx")).ShouldBeTrue();
            RepositoryTrustContext.IsExplicitlyTrustedFor(Path.Combine(secondRoot, "App.slnx")).ShouldBeTrue();
            RepositoryTrustContext.IsExplicitlyTrustedFor(Path.Combine(untrustedRoot, "App.slnx")).ShouldBeFalse();
        }
    }

    [Fact]
    public void empty_allowlist_denies_access_even_inside_an_unrestricted_outer_scope()
    {
        var solutionPath = Path.Combine(Path.GetTempPath(), "App.slnx");

        using (RepositoryTrustContext.UseExplicitTrust())
        {
            using (RepositoryTrustContext.UseExplicitTrust(Array.Empty<string>()))
            {
                RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
                RepositoryTrustContext.IsExplicitlyTrustedFor(solutionPath).ShouldBeFalse();
            }

            RepositoryTrustContext.IsExplicitlyTrustedFor(solutionPath).ShouldBeTrue();
        }

        RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
    }

    [Fact]
    public void invalid_allowlist_does_not_replace_the_previous_scope()
    {
        var root = Path.Combine(Path.GetTempPath(), "outer");
        var otherRoot = Path.Combine(Path.GetTempPath(), "other");

        using (RepositoryTrustContext.UseExplicitTrust(root))
        {
            Should.Throw<ArgumentException>(() => RepositoryTrustContext.UseExplicitTrust([otherRoot, "relative"]));

            RepositoryTrustContext.IsExplicitlyTrustedFor(Path.Combine(root, "App.slnx")).ShouldBeTrue();
            RepositoryTrustContext.IsExplicitlyTrustedFor(Path.Combine(otherRoot, "App.slnx")).ShouldBeFalse();
        }
    }

    [Fact]
    public void allowlist_is_snapshotted_and_normalized_before_authorization()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"rt-trust-{Guid.NewGuid():N}");
        var root = Path.Combine(parent, "repo");
        var roots = new List<string> { Path.Combine(root, ".") };

        using (RepositoryTrustContext.UseExplicitTrust(roots))
        {
            roots.Add(parent);
            roots.Clear();

            RepositoryTrustContext.IsExplicitlyTrustedFor(Path.Combine(root, "App.slnx")).ShouldBeTrue();
            RepositoryTrustContext.IsExplicitlyTrustedFor(Path.Combine(root, "..", "outside", "App.slnx")).ShouldBeFalse();
        }
    }

    [Fact]
    public void nested_scope_restores_previous_roots_and_disposal_is_idempotent()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"rt-trust-{Guid.NewGuid():N}");
        var outerRoot = Path.Combine(parent, "outer");
        var innerRoot = Path.Combine(parent, "inner");

        using (RepositoryTrustContext.UseExplicitTrust(outerRoot))
        {
            var innerScope = RepositoryTrustContext.UseExplicitTrust(new[] { innerRoot });
            RepositoryTrustContext.IsExplicitlyTrustedFor(outerRoot).ShouldBeFalse();
            RepositoryTrustContext.IsExplicitlyTrustedFor(innerRoot).ShouldBeTrue();

            innerScope.Dispose();
            RepositoryTrustContext.IsExplicitlyTrustedFor(outerRoot).ShouldBeTrue();
            RepositoryTrustContext.IsExplicitlyTrustedFor(innerRoot).ShouldBeFalse();

            using (RepositoryTrustContext.UseExplicitTrust(innerRoot))
            {
                innerScope.Dispose();
                RepositoryTrustContext.IsExplicitlyTrustedFor(innerRoot).ShouldBeTrue();
            }
        }

        RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
    }
}
