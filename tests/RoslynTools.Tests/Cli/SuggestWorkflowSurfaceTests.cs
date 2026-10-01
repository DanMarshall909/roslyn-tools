using System.Text.Json;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Cli;

public sealed class SuggestWorkflowSurfaceTests
{
    [Fact]
    public async Task suggest_help_exposes_optional_solution_for_populated_routes()
    {
        var help = await RtCliHost.RunAsync("suggest --help");

        help.ExitCode.ShouldBe(0);
        help.Stdout.ShouldContain("--solution");
    }

    [Fact]
    public async Task implementation_search_json_contains_an_invokable_route_with_solution()
    {
        var result = await RtCliHost.RunAsync(
            "suggest \"Find every implementation of IRtCommand\" --solution RoslynTools.slnx --top 3 --json");

        result.ExitCode.ShouldBe(0);
        using var payload = JsonDocument.Parse(result.Stdout);
        var response = payload.RootElement;
        response.GetProperty("Outcome").GetString().ShouldBe("Recommendations");
        response.GetProperty("Results").GetArrayLength().ShouldBe(0);
        var step = response.GetProperty("Workflow").GetProperty("Steps")[0];
        step.GetProperty("CliInvocation").GetString().ShouldBe(
            $"rt inspect relationships implementations IRtCommand --solution {Path.Combine(RtCliHost.RepoRoot, "RoslynTools.slnx")} --json");
        step.GetProperty("McpArguments").GetProperty("symbol").GetString().ShouldBe("IRtCommand");
        step.GetProperty("McpArguments").GetProperty("solution").GetString().ShouldBe(
            Path.Combine(RtCliHost.RepoRoot, "RoslynTools.slnx"));
    }

    [Fact]
    public async Task implementation_search_text_shows_route_and_completeness_stop_condition()
    {
        var result = await RtCliHost.RunAsync(
            "suggest \"Find every implementation of IRtCommand\" --solution RoslynTools.slnx");

        result.ExitCode.ShouldBe(0);
        result.Stdout.ShouldContain(
            $"rt inspect relationships implementations IRtCommand --solution {Path.Combine(RtCliHost.RepoRoot, "RoslynTools.slnx")} --json");
        result.Stdout.ShouldContain("Partial");
        result.Stdout.ShouldNotContain("extract-interface");
    }
}
