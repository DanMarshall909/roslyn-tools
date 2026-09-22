using RoslynTools.Mcp;
using RoslynTools.Core;
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
        options.TrustedRepositoryRoot.ShouldBeNull();
        options.TrustRegistry.ShouldNotBeNull();
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
        options.TrustedRepositoryRoot.ShouldBe(Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(repositoryRoot)));
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

    [Fact]
    public void live_persistent_record_enables_mutation_discovery_without_startup_path()
    {
        var registryPath = Path.Combine(
            Path.GetTempPath(),
            $"rt-mcp-trust-{Guid.NewGuid():N}",
            "repository-trust.json");
        try
        {
            var registry = new RepositoryTrustRegistry(registryPath);
            registry.Add(FindRepoRoot()).IsOk.ShouldBeTrue();

            var options = new McpHostOptions(trustRegistry: registry);

            options.CanDiscoverRepositoryWrites.ShouldBeTrue();
            using (options.EnterTrustScope())
                RepositoryTrustContext.IsExplicitlyTrustedFor(
                    Path.Combine(FindRepoRoot(), "RoslynTools.slnx")).ShouldBeTrue();
        }
        finally
        {
            var directory = Path.GetDirectoryName(registryPath)!;
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void persistent_write_discovery_tracks_add_and_remove_after_host_options_are_created()
    {
        var registryPath = Path.Combine(
            Path.GetTempPath(),
            $"rt-mcp-trust-{Guid.NewGuid():N}",
            "repository-trust.json");
        try
        {
            var registry = new RepositoryTrustRegistry(registryPath);
            var options = new McpHostOptions(trustRegistry: registry);

            options.CanDiscoverRepositoryWrites.ShouldBeFalse();
            registry.Add(FindRepoRoot()).IsOk.ShouldBeTrue();
            options.CanDiscoverRepositoryWrites.ShouldBeTrue();
            registry.Remove(FindRepoRoot()).IsOk.ShouldBeTrue();
            options.CanDiscoverRepositoryWrites.ShouldBeFalse();
        }
        finally
        {
            var directory = Path.GetDirectoryName(registryPath)!;
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void corrupt_persistent_store_hides_mutations_and_fails_closed()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rt-mcp-trust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var registryPath = Path.Combine(directory, "repository-trust.json");
            File.WriteAllText(registryPath, "{ invalid json");
            var options = new McpHostOptions(
                trustRegistry: new RepositoryTrustRegistry(registryPath));

            options.CanDiscoverRepositoryWrites.ShouldBeFalse();
            using (options.EnterTrustScope())
                RepositoryTrustContext.IsExplicitlyTrustedFor(
                    Path.Combine(FindRepoRoot(), "RoslynTools.slnx")).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
