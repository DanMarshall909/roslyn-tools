using FluentAssertions;
using RoslynTools.Core;
using RoslynTools.Mcp.Handlers;
using RoslynTools.Mcp.Schemas;

namespace RoslynTools.Tests.Mcp.Handlers;

public class FindReferencesHandlerTests
{
    [Fact]
    public async Task Returns_reference_count_for_referenced_type()
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
        {
            var handler = new FindReferencesHandler();
            var request = new FindReferencesRequest(Symbol: "Invoice", Solution: "ignored.slnx");

            var result = await handler.HandleAsync(request);

            result.Symbol.Should().Be("Invoice");
            result.ReferencesCount.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task Returns_zero_for_unreferenced_symbol()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Orphan.cs", """
            namespace TestApp;

            public class NeverUsed { }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var handler = new FindReferencesHandler();
            var request = new FindReferencesRequest(Symbol: "NeverUsed", Solution: "ignored.slnx");

            var result = await handler.HandleAsync(request);

            result.Symbol.Should().Be("NeverUsed");
            result.ReferencesCount.Should().Be(0);
        }
    }
}
