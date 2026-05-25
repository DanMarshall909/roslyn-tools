using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Mcp.Tools;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

[Collection("mcp-bridge")]
public class FindRefsToolTests
{
    [Fact]
    public async Task find_refs_returns_structured_references_for_referenced_type()
    {
        using var project = McpTestHost.CreateInvoiceProject(
            "public int Id { get; set; }",
            """
            public Invoice Create() => new Invoice();
            public void Process(Invoice invoice) { }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        using (var provider = McpTestHost.CreateProvider())
        {
            var registry = provider.GetRequiredService<ToolRegistry>();
            using var args = JsonDocument.Parse("""
                                                {"symbol":"Invoice","solution":"ignored.slnx"}
                                                """);

            var result = await registry.InvokeAsync("find_refs", args.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("symbolName").GetString().ShouldBe("Invoice");
            payload.RootElement.GetProperty("references").GetArrayLength().ShouldBeGreaterThan(0);

            var first = payload.RootElement.GetProperty("references")[0];
            first.GetProperty("filePath").GetString().ShouldNotBeNullOrEmpty();
            first.GetProperty("line").GetInt32().ShouldBeGreaterThan(0);
            first.GetProperty("lineText").GetString().ShouldNotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task find_refs_returns_empty_array_for_unreferenced_symbol()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Orphan.cs", """
                                       namespace TestApp;

                                       public class NeverUsed { }
                                       """);

        var (workspace, sln) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        using (var provider = McpTestHost.CreateProvider())
        {
            var registry = provider.GetRequiredService<ToolRegistry>();
            using var args = JsonDocument.Parse("""
                                                {"symbol":"NeverUsed","solution":"ignored.slnx"}
                                                """);

            var result = await registry.InvokeAsync("find_refs", args.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("symbolName").GetString().ShouldBe("NeverUsed");
            payload.RootElement.GetProperty("references").GetArrayLength().ShouldBe(0);
        }
    }

    [Fact]
    public async Task find_refs_returns_error_when_symbol_missing()
    {
        using var provider = McpTestHost.CreateProvider();
        var registry = provider.GetRequiredService<ToolRegistry>();
        using var args = JsonDocument.Parse("""{"solution":"ignored.slnx"}""");

        var result = await registry.InvokeAsync("find_refs", args.RootElement);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain("symbol");
    }
}
