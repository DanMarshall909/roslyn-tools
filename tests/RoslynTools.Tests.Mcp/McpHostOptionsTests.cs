using RoslynTools.Core;
using RoslynTools.Mcp;
using Shouldly;

namespace RoslynTools.Tests.Mcp;

public sealed class McpHostOptionsTests
{
    [Fact]
    public void no_arguments_leave_repository_evaluation_unapproved()
    {
        var parsed = McpHostOptions.TryParse([], out var options, out var error);

        parsed.ShouldBeTrue();
        error.ShouldBeNull();
        options.TrustedRepositoryRoots.ShouldBeEmpty();
        options.HasExplicitTrust.ShouldBeFalse();
    }

    [Fact]
    public void trust_repository_requires_and_normalizes_an_existing_absolute_directory()
    {
        var repositoryRoot = FindRepoRoot();

        var parsed = McpHostOptions.TryParse(
            ["--trust-repository", repositoryRoot],
            out var options,
            out var error);

        parsed.ShouldBeTrue();
        error.ShouldBeNull();
        options.TrustedRepositoryRoots.ShouldBe([Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(repositoryRoot))]);
        options.HasExplicitTrust.ShouldBeTrue();
    }

    [Fact]
    public void repeated_trust_repository_options_allow_multiple_normalized_roots()
    {
        var firstRoot = FindRepoRoot();
        var secondRoot = Path.GetTempPath();

        var parsed = McpHostOptions.TryParse(
            ["--trust-repository", firstRoot, "--trust-repository", secondRoot],
            out var options,
            out var error);

        parsed.ShouldBeTrue();
        error.ShouldBeNull();
        options.TrustedRepositoryRoots.ShouldBe([
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(firstRoot)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(secondRoot))
        ]);
    }

    [Fact]
    public void duplicate_normalized_roots_are_retained_once()
    {
        var root = FindRepoRoot();

        var parsed = McpHostOptions.TryParse(
            ["--trust-repository", root, "--trust-repository", Path.Combine(root, ".")],
            out var options,
            out var error);

        parsed.ShouldBeTrue();
        error.ShouldBeNull();
        options.TrustedRepositoryRoots.ShouldBe([Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))]);
    }

    [Fact]
    public void host_scope_authorizes_each_configured_root_and_restores_prior_authority()
    {
        var firstRoot = FindRepoRoot();
        var secondRoot = Path.GetTempPath();
        var options = new McpHostOptions(new[] { firstRoot, secondRoot });

        using (options.EnterTrustScope())
        {
            RepositoryTrustContext.IsExplicitlyTrustedFor(Path.Combine(firstRoot, "App.slnx")).ShouldBeTrue();
            RepositoryTrustContext.IsExplicitlyTrustedFor(Path.Combine(secondRoot, "Other.slnx")).ShouldBeTrue();
        }

        RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
    }

    [Fact]
    public void unapproved_host_scope_masks_ambient_authority()
    {
        using (RepositoryTrustContext.UseExplicitTrust())
        {
            using (new McpHostOptions().EnterTrustScope())
            {
                RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
                RepositoryTrustContext.IsExplicitlyTrustedFor(FindRepoRoot()).ShouldBeFalse();
            }

            RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeTrue();
        }
    }

    [Fact]
    public void constructor_snapshots_the_root_list_and_exposes_it_read_only()
    {
        var root = FindRepoRoot();
        var roots = new List<string> { root };
        var options = new McpHostOptions(roots);

        roots.Clear();

        options.TrustedRepositoryRoots.Count.ShouldBe(1);
        Should.Throw<NotSupportedException>(() => ((IList<string>)options.TrustedRepositoryRoots).Clear());
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("")]
    public void invalid_second_root_rejects_the_entire_allowlist(string invalidRoot)
    {
        var parsed = McpHostOptions.TryParse(
            ["--trust-repository", FindRepoRoot(), "--trust-repository", invalidRoot],
            out var options,
            out var error);

        parsed.ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();
        options.HasExplicitTrust.ShouldBeFalse();
        options.TrustedRepositoryRoots.ShouldBeEmpty();
    }

    [Fact]
    public void nonexistent_second_root_rejects_the_entire_allowlist()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), $"rt-missing-root-{Guid.NewGuid():N}");

        var parsed = McpHostOptions.TryParse(
            ["--trust-repository", FindRepoRoot(), "--trust-repository", missingRoot],
            out var options,
            out var error);

        parsed.ShouldBeFalse();
        error!.ShouldContain("does not exist");
        options.HasExplicitTrust.ShouldBeFalse();
    }

    [Fact]
    public void malformed_second_root_rejects_the_entire_allowlist()
    {
        var parsed = McpHostOptions.TryParse(
            ["--trust-repository", FindRepoRoot(), "--trust-repository", Path.GetTempPath() + "\0invalid"],
            out var options,
            out var error);

        parsed.ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();
        options.HasExplicitTrust.ShouldBeFalse();
    }

    [Fact]
    public void missing_value_after_a_valid_root_rejects_the_entire_allowlist()
    {
        var parsed = McpHostOptions.TryParse(
            ["--trust-repository", FindRepoRoot(), "--trust-repository"],
            out var options,
            out var error);

        parsed.ShouldBeFalse();
        error!.ShouldContain("requires one absolute");
        options.HasExplicitTrust.ShouldBeFalse();
    }

    [Fact]
    public void unknown_option_after_a_valid_root_rejects_the_entire_allowlist()
    {
        var parsed = McpHostOptions.TryParse(
            ["--trust-repository", FindRepoRoot(), "--trusted", Path.GetTempPath()],
            out var options,
            out var error);

        parsed.ShouldBeFalse();
        error!.ShouldContain("Unknown option");
        options.HasExplicitTrust.ShouldBeFalse();
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("")]
    public void trust_repository_rejects_a_non_absolute_root(string repositoryRoot)
    {
        var parsed = McpHostOptions.TryParse(
            ["--trust-repository", repositoryRoot],
            out _,
            out var error);

        parsed.ShouldBeFalse();
        error!.ShouldContain("absolute");
    }

    [Fact]
    public void unknown_startup_arguments_are_rejected()
    {
        var parsed = McpHostOptions.TryParse(["--trusted", FindRepoRoot()], out _, out var error);

        parsed.ShouldBeFalse();
        error!.ShouldContain("Unknown option");
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "RoslynTools.slnx")))
                return directory.FullName;

        throw new InvalidOperationException("Could not locate RoslynTools.slnx.");
    }
}
