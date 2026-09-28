using RoslynTools.Services.GitHub;
using Shouldly;

namespace RoslynTools.Tests.Architecture;

public sealed class HostProcessPlacementTests
{
    [Fact]
    public void github_cli_adapter_belongs_to_the_cli_host()
    {
        typeof(GitHubIssueClient).Assembly.ShouldBe(typeof(App).Assembly);
    }
}
