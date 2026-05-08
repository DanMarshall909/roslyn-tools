using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;

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

        Assert.NotNull(provider.GetRequiredService<ToolSchemaBuilder>());
        Assert.NotNull(provider.GetRequiredService<CliCommandBridge>());
        Assert.NotNull(provider.GetRequiredService<ToolRegistry>());
        Assert.NotNull(provider.GetRequiredService<Server>());
    }

    [Fact]
    public void Tool_schema_builder_produces_definition_for_every_IRtCommand()
    {
        var builder = new ToolSchemaBuilder();
        var tools = builder.Build();

        var cliCommandCount = App.Commands.Count;
        Assert.Equal(cliCommandCount, tools.Count);
        Assert.All(tools, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.description));
            Assert.NotNull(t.inputSchema);
            Assert.Equal("object", t.inputSchema["type"]);
        });
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

        Assert.Empty(directUsages);
    }

    [Fact]
    public async Task Tool_registry_invokes_find_refs_via_bridge()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Model.cs", """
                                      namespace TestApp;

                                      public class Invoice
                                      {
                                      }
                                      """);
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class InvoiceService
                                        {
                                            public Invoice Create() => new Invoice();
                                        }
                                        """);

        var (workspace, sln) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        using (var provider = CreateProvider())
        {
            var registry = provider.GetRequiredService<ToolRegistry>();
            using var args = JsonDocument.Parse("""
                                                {
                                                  "symbol": "Invoice",
                                                  "solution": "ignored.slnx"
                                                }
                                                """);

            var result = await registry.InvokeAsync("find_refs", args.RootElement);

            Assert.False(result.isError);
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

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(services);
        services.AddMcpServices(new StringReader(string.Empty), new StringWriter());
        return services.BuildServiceProvider();
    }
}
