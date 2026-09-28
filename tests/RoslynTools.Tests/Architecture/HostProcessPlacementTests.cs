using RoslynTools.Services.GitHub;
using RoslynTools.Services.SemanticDiff.Git;
using Shouldly;

namespace RoslynTools.Tests.Architecture;

public sealed class HostProcessPlacementTests
{
    [Fact]
    public void github_cli_adapter_belongs_to_the_cli_host()
    {
        typeof(GitHubIssueClient).Assembly.ShouldBe(typeof(App).Assembly);
    }

    [Fact]
    public void semantic_diff_git_process_adapter_belongs_to_shared_hosting()
    {
        typeof(GitClient).Assembly.GetName().Name.ShouldBe("RoslynTools.Hosting");
        typeof(GitClientFactory).Assembly.ShouldBe(typeof(GitClient).Assembly);
    }
}
