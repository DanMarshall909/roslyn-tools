using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Mcp;
using RoslynTools.Tests;

namespace RoslynTools.Tests.Mcp;

internal static class McpTestHost
{
    internal static AdhocProjectBuilder CreateInvoiceProject(
        string invoiceMembers = "",
        string serviceMembers = "public Invoice Create() => new Invoice();")
    {
        var project = new AdhocProjectBuilder();
        project.AddSource("Model.cs", $$"""
                                      namespace TestApp;

                                      public class Invoice
                                      {
                                          {{invoiceMembers}}
                                      }
                                      """);
        project.AddSource("Service.cs", $$"""
                                        namespace TestApp;

                                        public class InvoiceService
                                        {
                                            {{serviceMembers}}
                                        }
                                        """);
        return project;
    }

    internal static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(services);
        services.AddMcpServices(new StringReader(string.Empty), new StringWriter());
        return services.BuildServiceProvider();
    }
}
