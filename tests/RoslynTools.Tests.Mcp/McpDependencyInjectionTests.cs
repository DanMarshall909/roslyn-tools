using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;
using Shouldly;

namespace RoslynTools.Tests.Mcp;

[Collection("mcp-bridge")]
public sealed class McpDependencyInjectionTests
{
    [Fact]
    public void Mcp_registers_cli_services_and_mcp_runtime_services()
    {
        var services = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(services);
        services.AddMcpServices(new StringReader(string.Empty), new StringWriter());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ToolSchemaBuilder>().ShouldNotBeNull();
        provider.GetRequiredService<CliCommandBridge>().ShouldNotBeNull();
        provider.GetRequiredService<McpCommandAdapter>().ShouldNotBeNull();
        provider.GetRequiredService<ToolRegistry>().ShouldNotBeNull();
        provider.GetRequiredService<Server>().ShouldNotBeNull();
    }

    [Fact]
    public void Tool_schema_builder_produces_definition_for_every_IRtCommand()
    {
        using var provider = McpTestHost.CreateProvider();
        var builder = provider.GetRequiredService<ToolSchemaBuilder>();
        var tools = builder.Build();

        var cliCommandCount = App.Commands.Count;
        tools.Count.ShouldBe(cliCommandCount + 3);
        Assert.All(tools, t =>
        {
            string.IsNullOrWhiteSpace(t.name).ShouldBeFalse();
            string.IsNullOrWhiteSpace(t.description).ShouldBeFalse();
            t.inputSchema.ShouldNotBeNull();
            t.inputSchema["type"].ShouldBe("object");
        });
    }

    [Fact]
    public void Tool_schema_builder_models_debug_break_and_assert_as_arrays()
    {
        using var provider = McpTestHost.CreateProvider();
        var builder = provider.GetRequiredService<ToolSchemaBuilder>();

        var debug = builder.Build().Single(tool => tool.name == "debug");
        var properties = debug.inputSchema["properties"].ShouldBeOfType<Dictionary<string, Dictionary<string, object>>>();

        AssertArrayOfStrings(properties["break"]);
        AssertArrayOfStrings(properties["assert"]);
    }

    [Fact]
    public async Task Tool_registry_expands_debug_break_and_assert_arrays()
    {
        var trace = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(
            trace,
            """
            {"event":"tool_call","tool":"apply_patch","files":["src/Foo.cs"],"summary":"edit file"}
            {"event":"final_response","content":"done"}
            """);

        using var provider = McpTestHost.CreateProvider();
        var registry = provider.GetRequiredService<ToolRegistry>();
        using var args = JsonDocument.Parse(
            $$"""
              {
                "trace": "{{JsonEncodedText.Encode(trace)}}",
                "break": ["tool:apply_patch", "file:src/Foo.cs"],
                "assert": ["requires-event:final_response", "no-tool:web"],
                "break-only": true
              }
              """);

        var result = await registry.InvokeAsync("debug", args.RootElement);

        result.isError.ShouldBeFalse();
        using var output = JsonDocument.Parse(result.content);
        var root = output.RootElement;
        root.GetProperty("breakpointHits").GetArrayLength().ShouldBe(2);
        Assert.All(root.GetProperty("assertions").EnumerateArray(), assertion =>
            assertion.GetProperty("passed").GetBoolean().ShouldBeTrue());
    }

    [Fact]
    public void Mcp_runtime_does_not_use_static_service_locator_or_static_workspace_loading()
    {
        var repoRoot = FindRepoRoot();
        var sourceFiles = Directory.EnumerateFiles(
            Path.Combine(repoRoot, "src", "RoslynTools.Mcp"),
            "*.cs",
            SearchOption.AllDirectories);

        var directUsages = sourceFiles
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("RtServiceProvider.Get", StringComparison.Ordinal)
                       || text.Contains("WorkspaceLoader.LoadAsync", StringComparison.Ordinal)
                       || text.Contains("SolutionWriter.ApplyChangesAsync", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        directUsages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Tool_registry_invokes_find_refs_via_bridge()
    {
        using var project = McpTestHost.CreateInvoiceProject();

        var (workspace, sln) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        using (var provider = McpTestHost.CreateProvider())
        {
            var registry = provider.GetRequiredService<ToolRegistry>();
            using var args = JsonDocument.Parse("""
                                                {
                                                  "symbol": "Invoice",
                                                  "solution": "ignored.slnx"
                                                }
                                                """);

            var result = await registry.InvokeAsync("find_refs", args.RootElement);

            result.isError.ShouldBeFalse();
            Assert.Contains("Invoice", result.content, StringComparison.Ordinal);
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RoslynTools.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static void AssertArrayOfStrings(Dictionary<string, object> schema)
    {
        schema["type"].ShouldBe("array");
        var items = schema["items"].ShouldBeOfType<Dictionary<string, object>>();
        items["type"].ShouldBe("string");
    }

}
