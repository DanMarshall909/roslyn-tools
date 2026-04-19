using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Tests;

public class FindRefsTests
{
    [Fact]
    public async Task Finds_References_To_Type_Across_Files()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Model.cs", """
            namespace TestApp;

            public class Invoice
            {
                public int Id { get; set; }
                public decimal Amount { get; set; }
            }
            """);
        project.AddSource("Service.cs", """
            namespace TestApp;

            public class InvoiceService
            {
                public Invoice Create(decimal amount)
                {
                    return new Invoice { Amount = amount };
                }

                public void Process(Invoice invoice) { }
            }
            """);
        project.AddSource("Controller.cs", """
            namespace TestApp;

            public class InvoiceController
            {
                private readonly InvoiceService _service = new();

                public Invoice Get(int id) => new Invoice();
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.Invoice", "type");
            var refs = await SymbolFinder.FindReferencesAsync(sym, sln);

            var locations = refs.SelectMany(r => r.Locations).ToList();

            // Invoice is referenced in Service.cs (return type, new, parameter) and Controller.cs (field type, return type, new)
            Assert.True(locations.Count >= 4, $"Expected at least 4 references, got {locations.Count}");

            var files = locations
                .Select(l => Path.GetFileName(l.Document.FilePath!))
                .Distinct()
                .ToList();

            Assert.Contains("Service.cs", files);
            Assert.Contains("Controller.cs", files);
        }
    }

    [Fact]
    public async Task Finds_References_To_Method()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("App.cs", """
            namespace TestApp;

            public static class MathHelper
            {
                public static int Square(int x) => x * x;
            }

            public class Consumer
            {
                public int DoWork()
                {
                    var a = MathHelper.Square(3);
                    var b = MathHelper.Square(5);
                    return a + b;
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.MathHelper.Square", "method");
            var refs = await SymbolFinder.FindReferencesAsync(sym, sln);

            var locations = refs.SelectMany(r => r.Locations).ToList();
            Assert.Equal(2, locations.Count); // Two call sites
        }
    }

    [Fact]
    public async Task Unreferenced_Symbol_Returns_No_Locations()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Unused.cs", """
            namespace TestApp;

            public class NeverUsed
            {
                public void Orphan() { }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.NeverUsed", "type");
            var refs = await SymbolFinder.FindReferencesAsync(sym, sln);

            var locations = refs.SelectMany(r => r.Locations).ToList();
            Assert.Empty(locations);
        }
    }
}
