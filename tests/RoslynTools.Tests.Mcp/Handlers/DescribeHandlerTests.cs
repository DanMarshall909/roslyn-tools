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
    public async Task describe_returns_compact_signature_array_for_a_known_type()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public sealed class Widget
                                       {
                                           public string Label { get; set; } = "";
                                           public void Activate() { }
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
            payload.RootElement.GetProperty("fqn").GetString().ShouldBe("TestApp.Widget");
            payload.RootElement.GetProperty("kind").GetString().ShouldBe("Class");
            var signatures = payload.RootElement.GetProperty("signatures")
                .EnumerateArray().Select(e => e.GetString()).ToList();
            signatures.ShouldContain("void Activate()");
            payload.RootElement.TryGetProperty("name", out _).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task describe_with_locations_returns_verbose_shape()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public sealed class Widget { public string Label { get; set; } = ""; }
                                       """);

        var (workspace, sln) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        using (var provider = McpTestHost.CreateProvider())
        {
            var registry = provider.GetRequiredService<ToolRegistry>();
            using var args = JsonDocument.Parse("""
                                                {"symbol":"TestApp.Widget","kind":"type","solution":"ignored.slnx","withLocations":true}
                                                """);

            var result = await registry.InvokeAsync("describe", args.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("name").GetString().ShouldBe("Widget");
            payload.RootElement.TryGetProperty("locations", out _).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task describe_drills_into_member_by_signature()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Calc.cs", """
                                     namespace TestApp;

                                     public class Calculator
                                     {
                                         public int Add(int a, int b) => a + b;
                                         public double Add(double a, double b) => a + b;
                                     }
                                     """);

        var (workspace, sln) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        using (var provider = McpTestHost.CreateProvider())
        {
            var registry = provider.GetRequiredService<ToolRegistry>();
            using var args = JsonDocument.Parse("""
                                                {"symbol":"TestApp.Calculator.Add(int, int)","solution":"ignored.slnx"}
                                                """);

            var result = await registry.InvokeAsync("describe", args.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("name").GetString().ShouldBe("Add");
            payload.RootElement.GetProperty("returnType").GetString().ShouldBe("int");
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
            using var error = JsonDocument.Parse(result.content);
            error.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.NotFound));
            error.RootElement.GetProperty("message").GetString()
                .ShouldBe("No symbol with FQN 'TestApp.Missing' found in solution.");
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
