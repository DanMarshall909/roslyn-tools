using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Mcp.Tools;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

[Collection("mcp-bridge")]
public class DescribeToolTests
{
    [Fact]
    public async Task describe_returns_inspection_json_for_a_known_type()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public sealed class Widget
                                       {
                                           public string Label { get; set; } = "";
                                       }
                                       """);

        var (workspace, sln) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        using (var provider = McpTestHost.CreateProvider())
        {
            var registry = provider.GetRequiredService<ToolRegistry>();
            using var args = JsonDocument.Parse("""
                                                {"symbol":"TestApp.Widget","kind":"type","solution":"ignored.slnx"}
                                                """);

            var result = await registry.InvokeAsync("describe", args.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("name").GetString().ShouldBe("Widget");
            payload.RootElement.GetProperty("kind").GetString().ShouldBe("NamedType");
            payload.RootElement.GetProperty("fqn").GetString().ShouldBe("TestApp.Widget");
        }
    }

    [Fact]
    public async Task describe_returns_error_for_an_unknown_symbol()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public sealed class Widget { }
                                       """);

        var (workspace, sln) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        using (var provider = McpTestHost.CreateProvider())
        {
            var registry = provider.GetRequiredService<ToolRegistry>();
            using var args = JsonDocument.Parse("""
                                                {"symbol":"TestApp.Missing","kind":"type","solution":"ignored.slnx"}
                                                """);

            var result = await registry.InvokeAsync("describe", args.RootElement);

            result.isError.ShouldBeTrue();
            result.content.ShouldBe("No symbol with FQN 'TestApp.Missing' found in solution.");
        }
    }

    [Fact]
    public async Task describe_returns_error_when_symbol_missing()
    {
        using var provider = McpTestHost.CreateProvider();
        var registry = provider.GetRequiredService<ToolRegistry>();
        using var args = JsonDocument.Parse("""{"solution":"ignored.slnx"}""");

        var result = await registry.InvokeAsync("describe", args.RootElement);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain("symbol");
    }
}
