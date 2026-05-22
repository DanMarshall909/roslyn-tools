using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Mcp.Tools;
using RoslynTools.Tests.Mcp;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

[Collection("mcp-bridge")]
public class FindRefsToolTests
{
    [Fact]
    public async Task find_refs_returns_output_for_referenced_type()
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
            Assert.Contains("Invoice", result.content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task find_refs_succeeds_for_unreferenced_symbol()
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
        }
    }

}
