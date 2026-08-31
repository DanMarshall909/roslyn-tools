using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Mcp.Tools;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

[Collection("mcp-bridge")]
public class RelationshipsReferencesToolTests
{
    [Fact]
    public async Task relationships_returns_structured_references_for_referenced_type()
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
                                                {"symbol":"Invoice","mode":"References","kind":"type","solution":"ignored.slnx"}
                                                """);

            var result = await registry.InvokeAsync("relationships", args.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("operation").GetString().ShouldBe("relationships");
            var data = payload.RootElement.GetProperty("data");
            data.GetProperty("resolvedSubject").GetString().ShouldBe("TestApp.Invoice");
            data.GetProperty("items").GetArrayLength().ShouldBeGreaterThan(0);

            var first = data.GetProperty("items")[0];
            first.GetProperty("relation").GetString().ShouldBe("Reference");
            first.GetProperty("file").GetString().ShouldNotBeNullOrEmpty();
            first.GetProperty("line").GetInt32().ShouldBeGreaterThan(0);
            first.GetProperty("evidence").GetString().ShouldNotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task relationships_returns_empty_array_for_unreferenced_symbol()
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
                                                {"symbol":"NeverUsed","mode":"References","kind":"type","solution":"ignored.slnx"}
                                                """);

            var result = await registry.InvokeAsync("relationships", args.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            var data = payload.RootElement.GetProperty("data");
            data.GetProperty("resolvedSubject").GetString().ShouldBe("TestApp.NeverUsed");
            data.GetProperty("items").GetArrayLength().ShouldBe(0);
        }
    }

    [Fact]
    public async Task relationships_returns_error_when_symbol_missing()
    {
        using var provider = McpTestHost.CreateProvider();
        var registry = provider.GetRequiredService<ToolRegistry>();
        using var args = JsonDocument.Parse("""{"mode":"References","solution":"ignored.slnx"}""");

        var result = await registry.InvokeAsync("relationships", args.RootElement);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain("symbol");
    }
}
