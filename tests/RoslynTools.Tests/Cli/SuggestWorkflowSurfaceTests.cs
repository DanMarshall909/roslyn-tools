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

    [Theory]
    [InlineData("Find all references to WorkspaceLoader", "WorkspaceLoader", null)]
    [InlineData("Find references to RoslynTools.Services.Suggest.SuggestService._catalog", "RoslynTools.Services.Suggest.SuggestService._catalog", null)]
    [InlineData("Find references to the field named _catalog", "_catalog", "field")]
    [InlineData("Find references to the method named Plan", "Plan", "method")]
    public async Task named_reference_search_has_one_cli_and_mcp_read_step(string query, string symbol, string? kind)
    {
        var json = await RtCliHost.RunAsync(
            $"suggest \"{query}\" --solution RoslynTools.slnx --top 3 --json");
        var text = await RtCliHost.RunAsync(
            $"suggest \"{query}\" --solution RoslynTools.slnx --top 3 --llm");

        json.ExitCode.ShouldBe(0, json.Stderr);
        text.ExitCode.ShouldBe(0, text.Stderr);
        using var payload = JsonDocument.Parse(json.Stdout);
        var response = payload.RootElement;
        response.GetProperty("Outcome").GetString().ShouldBe("Recommendations");
        response.GetProperty("Results").GetArrayLength().ShouldBe(0);
        response.GetProperty("Related").GetArrayLength().ShouldBe(0);
        var step = response.GetProperty("Workflow").GetProperty("Steps")[0];
        var solution = Path.Combine(RtCliHost.RepoRoot, "RoslynTools.slnx");
        var invocation = $"rt inspect relationships references {symbol}"
                         + (kind is null ? "" : $" --kind {kind}")
                         + $" --solution {solution} --json";
        step.GetProperty("CliInvocation").GetString().ShouldBe(invocation);
        step.GetProperty("McpTool").GetString().ShouldBe("relationships");
        step.GetProperty("McpArguments").GetProperty("symbol").GetString().ShouldBe(symbol);
        step.GetProperty("McpArguments").GetProperty("mode").GetString().ShouldBe("References");
        step.GetProperty("McpArguments").GetProperty("solution").GetString().ShouldBe(solution);
        if (kind is null)
            step.GetProperty("McpArguments").TryGetProperty("kind", out _).ShouldBeFalse();
        else
            step.GetProperty("McpArguments").GetProperty("kind").GetString().ShouldBe(kind);
        step.GetProperty("StopCondition").GetString()!.ShouldContain("truncated");
        text.Stdout.ShouldContain(invocation);
        text.Stdout.ShouldContain("Partial");
        text.Stdout.ShouldNotContain("find-pattern");
    }

    [Theory]
    [InlineData("Find references to the constructor named WorkspaceLoader")]
    [InlineData("Find references to the namespace named RoslynTools")]
    public async Task unsupported_reference_kind_keeps_catalogue_discovery(string query)
    {
        var result = await RtCliHost.RunAsync($"suggest \"{query}\" --solution RoslynTools.slnx --json");

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var payload = JsonDocument.Parse(result.Stdout);
        payload.RootElement.TryGetProperty("Workflow", out _).ShouldBeFalse();
        payload.RootElement.GetProperty("Results").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task configured_architecture_check_has_a_populated_cli_and_mcp_route()
    {
        var json = await RtCliHost.RunAsync(
            "suggest \"Find violations of configured project layering rules\" --solution RoslynTools.slnx --json");
        var text = await RtCliHost.RunAsync(
            "suggest \"Find violations of configured project layering rules\" --solution RoslynTools.slnx --llm");

        json.ExitCode.ShouldBe(0, json.Stderr);
        text.ExitCode.ShouldBe(0, text.Stderr);
        using var payload = JsonDocument.Parse(json.Stdout);
        var response = payload.RootElement;
        response.GetProperty("Results").GetArrayLength().ShouldBe(0);
        var step = response.GetProperty("Workflow").GetProperty("Steps")[0];
        var solution = Path.Combine(RtCliHost.RepoRoot, "RoslynTools.slnx");
        step.GetProperty("CliInvocation").GetString().ShouldBe(
            $"rt analyze check-architecture --config .rt/architecture.json --solution {solution} --json");
        step.GetProperty("McpTool").GetString().ShouldBe("check_architecture");
        step.GetProperty("McpArguments").GetProperty("config").GetString()
            .ShouldBe(".rt/architecture.json");
        step.GetProperty("McpArguments").GetProperty("solution").GetString().ShouldBe(solution);
        text.Stdout.ShouldContain("check-architecture --config .rt/architecture.json");
        text.Stdout.ShouldContain("Stop:");
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
