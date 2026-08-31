using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
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
    public async Task Mcp_test_host_never_attaches_to_a_resident_daemon()
    {
        using var provider = McpTestHost.CreateProvider();

        var attempt = await provider.GetRequiredService<IDaemonRpcClient>().ExecuteAsync(
            "describe",
            JsonSerializer.SerializeToElement(new { symbol = "TestApp.Widget" }),
            CommandSafety.ReadOnly);

        attempt.State.ShouldBe(DaemonRpcAttemptState.UnavailableBeforeDispatch);
        attempt.DispatchStarted.ShouldBeFalse();
    }

    [Fact]
    public void Tool_schema_builder_produces_definition_for_every_mcp_visible_descriptor()
    {
        using var provider = McpTestHost.CreateProvider();
        var builder = provider.GetRequiredService<ToolSchemaBuilder>();
        var tools = builder.Build();

        var visibleDescriptorCount = provider
            .GetRequiredService<RoslynTools.CommandDescriptors.CommandDescriptorRegistry>()
            .All.Count(descriptor => descriptor.McpVisible);
        tools.Count.ShouldBe(visibleDescriptorCount);
        Assert.All(tools, t =>
        {
            string.IsNullOrWhiteSpace(t.name).ShouldBeFalse();
            string.IsNullOrWhiteSpace(t.description).ShouldBeFalse();
            t.inputSchema.ShouldNotBeNull();
            t.inputSchema["type"].ShouldBe("object");
            t.annotations.readOnlyHint.ShouldBeTrue();
            t.annotations.destructiveHint.ShouldBeFalse();
            t.metadata.ShouldContainKey("rt/maturity");
            t.metadata.ShouldContainKey("rt/effects");
            t.metadata["rt/audience"].ShouldBe("Public");
        });
    }

    [Fact]
    public void Tool_schema_builder_excludes_removed_debug_surface()
    {
        using var provider = McpTestHost.CreateProvider();
        var builder = provider.GetRequiredService<ToolSchemaBuilder>();

        builder.Build().ShouldNotContain(tool => tool.name == "debug");
    }

    [Fact]
    public async Task Tool_registry_rejects_removed_debug_surface()
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

        result.isError.ShouldBeTrue();
        result.content.ShouldContain("Unknown tool");
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
    public async Task Tool_registry_invokes_relationships_via_typed_port()
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
                                                  "mode": "References",
                                                  "kind": "type",
                                                  "solution": "ignored.slnx"
                                                }
                                                """);

            var result = await registry.InvokeAsync("relationships", args.RootElement);

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

}
