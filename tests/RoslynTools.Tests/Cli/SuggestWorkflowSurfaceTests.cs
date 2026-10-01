using System.Text.Json;
using RoslynTools.Services.SemanticDiff.Git;
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

    [Fact]
    public async Task working_tree_review_has_an_invokable_or_explicitly_unsupported_cli_route()
    {
        var json = await RtCliHost.RunAsync(
            "suggest \"review my working tree C# changes\" --solution RoslynTools.slnx --json");
        var text = await RtCliHost.RunAsync(
            "suggest \"review my working tree C# changes\" --solution RoslynTools.slnx --llm");

        json.ExitCode.ShouldBe(0);
        text.ExitCode.ShouldBe(0);
        using var document = JsonDocument.Parse(json.Stdout);
        var response = document.RootElement;
        response.GetProperty("Results").GetArrayLength().ShouldBe(0);
        if (!WorkingTreeReadCapability.IsAvailable)
        {
            response.GetProperty("Outcome").GetString().ShouldBe("Unsupported");
            response.GetProperty("MissingCapability").GetString().ShouldBe("semantic-diff-working-tree");
            text.Stdout.ShouldContain("Git refs or two .cs files");
            return;
        }

        response.GetProperty("Outcome").GetString().ShouldBe("Recommendations");
        var step = response.GetProperty("Workflow").GetProperty("Steps")[0];
        step.GetProperty("CliInvocation").GetString().ShouldBe(
            $"rt analyze semantic-diff --directory {RtCliHost.RepoRoot} --json");
        step.GetProperty("McpTool").GetString().ShouldBe("semantic_diff");
        step.GetProperty("McpArguments").GetProperty("directory").GetString()
            .ShouldBe(RtCliHost.RepoRoot);
        text.Stdout.ShouldContain("semantic-diff --directory");
        text.Stdout.ShouldContain("Stop:");
    }
}
