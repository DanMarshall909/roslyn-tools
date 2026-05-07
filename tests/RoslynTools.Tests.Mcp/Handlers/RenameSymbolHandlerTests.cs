using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;

namespace RoslynTools.Tests.Mcp.Handlers;

[Collection("mcp-bridge")]
public class RenameToolTests
{
    [Fact]
    public async Task rename_defaults_to_dry_run_without_applying_changes()
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
                                                  "symbol": "TestApp.Invoice",
                                                  "new-name": "Bill",
                                                  "solution": "ignored.slnx"
                                                }
                                                """);

            var result = await registry.InvokeAsync("rename", args.RootElement);

            Assert.False(result.isError);
            Assert.Contains("Bill", result.content, StringComparison.Ordinal);
        }
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        Bootstrap.RegisterServices(services);
        services.AddMcpServices(new StringReader(string.Empty), new StringWriter());
        return services.BuildServiceProvider();
    }
}
