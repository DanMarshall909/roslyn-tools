using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;

namespace RoslynTools.Tests.Mcp.Handlers;

[Collection("mcp-bridge")]
public class FindRefsToolTests
{
    [Fact]
    public async Task find_refs_returns_output_for_referenced_type()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Model.cs", """
                                      namespace TestApp;

                                      public class Invoice
                                      {
                                          public int Id { get; set; }
                                      }
                                      """);
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class InvoiceService
                                        {
                                            public Invoice Create() => new Invoice();
                                            public void Process(Invoice invoice) { }
                                        }
                                        """);

        var (workspace, sln) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        using (var provider = CreateProvider())
        {
            var registry = provider.GetRequiredService<ToolRegistry>();
            using var args = JsonDocument.Parse("""
                                                {"symbol":"Invoice","solution":"ignored.slnx"}
                                                """);

            var result = await registry.InvokeAsync("find_refs", args.RootElement);

            Assert.False(result.isError);
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
        using (var provider = CreateProvider())
        {
            var registry = provider.GetRequiredService<ToolRegistry>();
            using var args = JsonDocument.Parse("""
                                                {"symbol":"NeverUsed","solution":"ignored.slnx"}
                                                """);

            var result = await registry.InvokeAsync("find_refs", args.RootElement);

            Assert.False(result.isError);
        }
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(services);
        services.AddMcpServices(new StringReader(string.Empty), new StringWriter());
        return services.BuildServiceProvider();
    }
}
