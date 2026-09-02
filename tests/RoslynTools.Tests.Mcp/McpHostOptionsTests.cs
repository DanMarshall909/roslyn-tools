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
        options.TrustedRepositoryRoot.ShouldBeNull();
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
