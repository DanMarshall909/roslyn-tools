using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

public sealed class CiCheckHandlerTests
{
    [Fact]
    public async Task Untrusted_host_discovers_check_but_cannot_grant_trust_through_arguments()
    {
        using var provider = McpTestHost.CreateProvider();
        var tool = provider.GetRequiredService<ToolRegistry>().GetTools().Single(item => item.name == "check");
        tool.annotations.readOnlyHint.ShouldBeTrue();
        using var arguments = JsonDocument.Parse("{}");
        var result = await provider.GetRequiredService<ToolRegistry>().InvokeAsync("check", arguments.RootElement);
        result.isError.ShouldBeTrue();
        result.content.ShouldContain("TrustRequired");
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public async Task Trusted_host_returns_the_same_enforcement_result(int maximum, bool passed)
    {
        using var builder = new AdhocProjectBuilder("Demo");
        builder.AddSource("Value.cs", "namespace Demo; public class Value { public int Run(bool flag) { if (flag) return 1; return 0; } }");
        var (workspace, solution) = builder.Build();
        Directory.CreateDirectory(Path.Combine(builder.Directory, ".rt"));
        File.WriteAllText(Path.Combine(builder.Directory, ".rt", "ci.json"),
            JsonSerializer.Serialize(new { version = 1, rules = new[] { new { id = "maintainability.complexity", maxCyclomatic = maximum } } }));
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider(new McpHostOptions(builder.Directory)))
        using (var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new { solution = builder.SolutionPath })))
        {
            var result = await provider.GetRequiredService<ToolRegistry>().InvokeAsync("check", arguments.RootElement);
            result.isError.ShouldBeFalse();
            var payload = result.structuredContent!.Value;
            payload.GetProperty("passed").GetBoolean().ShouldBe(passed);
            payload.GetProperty("findingCount").GetInt32().ShouldBe(passed ? 0 : 1);
        }
    }

    [Fact]
    public async Task Malformed_policy_is_a_typed_mcp_failure()
    {
        using var builder = new AdhocProjectBuilder("Demo");
        builder.AddSource("Value.cs", "public class Value { }");
        var (workspace, solution) = builder.Build();
        Directory.CreateDirectory(Path.Combine(builder.Directory, ".rt"));
        File.WriteAllText(Path.Combine(builder.Directory, ".rt", "ci.json"), "{}");
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider(new McpHostOptions(builder.Directory)))
        using (var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new { solution = builder.SolutionPath })))
        {
            var result = await provider.GetRequiredService<ToolRegistry>().InvokeAsync("check", arguments.RootElement);
            result.isError.ShouldBeTrue();
            result.content.ShouldContain("version must be 1");
        }
    }
}
