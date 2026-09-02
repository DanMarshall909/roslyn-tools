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
}
