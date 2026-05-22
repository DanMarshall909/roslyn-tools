using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Mcp.Tools;
using RoslynTools.Tests.Mcp;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

[Collection("mcp-bridge")]
public class RenameToolTests
{
    [Fact]
    public async Task rename_defaults_to_dry_run_without_applying_changes()
    {
        using var project = McpTestHost.CreateInvoiceProject("public int Id { get; set; }");

        var (workspace, sln) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        using (var provider = McpTestHost.CreateProvider())
        {
            var registry = provider.GetRequiredService<ToolRegistry>();
            using var args = JsonDocument.Parse("""
                                                {
                                                  "symbol": "TestApp.Invoice",
                                                  "new-name": "Bill",
                                                  "solution": "ignored.slnx"
                                                }
                                                """);

            var result = await registry.InvokeAsync("rename", args.RootElement);

            result.isError.ShouldBeFalse();
            Assert.Contains("Bill", result.content, StringComparison.Ordinal);
        }
    }

}
