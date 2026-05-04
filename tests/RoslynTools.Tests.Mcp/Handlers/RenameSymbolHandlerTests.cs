using FluentAssertions;
using RoslynTools.Core;
using RoslynTools.Mcp.Handlers;
using RoslynTools.Mcp.Schemas;
using RoslynTools.Tests;

namespace RoslynTools.Tests.Mcp.Handlers;

public class RenameSymbolHandlerTests
{
    [Fact]
    public async Task Dry_run_reports_changed_files_without_applying()
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
        {
            var handler = new RenameSymbolHandler();
            var request = new RenameSymbolRequest(
                OldName: "TestApp.Invoice",
                NewName: "Bill",
                Solution: "ignored.slnx",
                Apply: false);

            var result = await handler.HandleAsync(request);

            result.Applied.Should().BeFalse();
            result.ChangedFiles.Should().BeGreaterThan(0);
        }
    }

}
