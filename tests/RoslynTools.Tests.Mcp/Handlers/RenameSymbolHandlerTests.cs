using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Mcp.Tools;
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
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("newName").GetString().ShouldBe("Bill");
            payload.RootElement.GetProperty("applied").GetBoolean().ShouldBeFalse();
            payload.RootElement.GetProperty("changedFiles").GetInt32().ShouldBeGreaterThan(0);
        }
    }

    [Fact]
    public async Task rename_returns_related_edits_as_an_opt_in_plan()
    {
        using var project = McpTestHost.CreateInvoiceProject(
            """
            // Invoice is the persisted business document.
            public string WindowClass => "InvoiceWindow";
            """);

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
            using var payload = JsonDocument.Parse(result.content);
            var edits = payload.RootElement.GetProperty("relatedRenamePlan");
            edits.EnumerateArray().ShouldContain(edit =>
                edit.GetProperty("kind").GetString() == "identifier"
                && edit.GetProperty("oldText").GetString() == "InvoiceService");
            edits.EnumerateArray().ShouldContain(edit =>
                edit.GetProperty("kind").GetString() == "string"
                && edit.GetProperty("oldText").GetString() == "InvoiceWindow");
            edits.EnumerateArray().ShouldContain(edit =>
                edit.GetProperty("kind").GetString() == "comment"
                && edit.GetProperty("prose").GetBoolean());
            payload.RootElement.GetProperty("selectedRelatedEdits").GetArrayLength().ShouldBe(0);
        }
    }

    [Fact]
    public async Task rename_returns_error_when_new_name_missing()
    {
        using var provider = McpTestHost.CreateProvider();
        var registry = provider.GetRequiredService<ToolRegistry>();
        using var args = JsonDocument.Parse("""{"symbol":"TestApp.Invoice","solution":"ignored.slnx"}""");

        var result = await registry.InvokeAsync("rename", args.RootElement);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain("new-name");
    }

    [Fact]
    public async Task rename_returns_error_when_symbol_missing()
    {
        using var provider = McpTestHost.CreateProvider();
        var registry = provider.GetRequiredService<ToolRegistry>();
        using var args = JsonDocument.Parse("""{"new-name":"Bill","solution":"ignored.slnx"}""");

        var result = await registry.InvokeAsync("rename", args.RootElement);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain("symbol");
    }
}
