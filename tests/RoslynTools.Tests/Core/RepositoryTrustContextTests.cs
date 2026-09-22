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
    public void repository_bound_trust_authorizes_a_prospective_nested_path()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rt-trust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var destination = Path.Combine(root, "Feature", "NewType.cs");

        try
        {
            using var trust = RepositoryTrustContext.UseExplicitTrust(root);

            RepositoryTrustContext.IsExplicitlyTrustedFor(destination).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void repository_bound_trust_rejects_a_path_through_an_existing_file()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rt-trust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var blockingFile = Path.Combine(root, "blocking-file");
        File.WriteAllText(blockingFile, "not a directory");

        try
        {
            using var trust = RepositoryTrustContext.UseExplicitTrust(root);

            Should.Throw<IOException>(() => RepositoryTrustContext.IsExplicitlyTrustedFor(
                Path.Combine(blockingFile, "NewType.cs")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
