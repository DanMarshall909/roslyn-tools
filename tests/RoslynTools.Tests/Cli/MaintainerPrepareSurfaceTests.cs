using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Cli;

public sealed class MaintainerPrepareSurfaceTests
{
    [Fact]
    public async Task Durable_prepare_is_available_only_through_the_maintainer_cli_route()
    {
        var rootHelp = await RtCliHost.RunAsync("--help");
        var maintainerHelp = await RtCliHost.RunAsync("maintainer --help");
        var explicitHelp = await RtCliHost.RunAsync("maintainer prepare-operation --help");
        var oldRoute = await RtCliHost.RunAsync("prepare-operation");

        rootHelp.ExitCode.ShouldBe(0);
        rootHelp.Stdout.ShouldNotContain("prepare-operation");
        maintainerHelp.ExitCode.ShouldBe(0);
        maintainerHelp.Stdout.ShouldContain("prepare-operation");
        explicitHelp.ExitCode.ShouldBe(0);
        explicitHelp.Stdout.ShouldContain("--repository-id");
        explicitHelp.Stdout.ShouldContain("protected local storage");
        oldRoute.ExitCode.ShouldNotBe(0);
        oldRoute.Stderr.ShouldContain("not a recognised command");
    }

    [Theory]
    [InlineData("Prepare a safe rename plan and verify it without applying")]
    [InlineData("Verify a prepared plan without applying")]
    public async Task Public_discovery_does_not_suggest_the_durable_coordinator(string task)
    {
        var suggestions = await RtCliHost.RunAsync($"suggest \"{task}\" --top 3 --json");
        var catalog = await RtCliHost.RunAsync("dump-help --compact");

        suggestions.ExitCode.ShouldBe(0);
        suggestions.Stdout.ShouldNotContain("prepare-operation");
        catalog.ExitCode.ShouldBe(0);
        catalog.Stdout.ShouldNotContain("prepare-operation");
    }
}
