using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Rename;
using RoslynTools.Core;

namespace RoslynTools.Tests;

[Collection("MSBuild")]
public class RenameTests
{
    [Fact]
    public async Task Rename_Type_Updates_Declaration_And_References()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Model.cs", """
            namespace TestApp;

            public class Product
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }
            """);
        project.AddSource("Service.cs", """
            namespace TestApp;

            public class ProductService
            {
                public Product GetProduct(int id) => new Product();
                public void SaveProduct(Product product) { }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.Product", "type");

            var options = new SymbolRenameOptions(
                RenameOverloads: false,
                RenameInStrings: false,
                RenameInComments: false,
                RenameFile: false);

            var newSln = await Renamer.RenameSymbolAsync(sln, sym, options, "Item");

            // Apply changes to disk (not dry-run)
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        // Verify files on disk were updated
        var modelContent = await project.ReadSourceAsync("Model.cs");
        Assert.Contains("public class Item", modelContent);
        Assert.DoesNotContain("class Product", modelContent);

        var serviceContent = await project.ReadSourceAsync("Service.cs");
        Assert.Contains("Item", serviceContent);
        // "Product" class was renamed to "Item", but "ProductService" class name stays
        Assert.DoesNotContain("new Product()", serviceContent);
        Assert.Contains("new Item()", serviceContent);
    }

    [Fact]
    public async Task Rename_Method_Updates_All_Call_Sites()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Util.cs", """
            namespace TestApp;

            public static class StringHelper
            {
                public static string Sanitize(string input) => input.Trim();
            }
            """);
        project.AddSource("Consumer.cs", """
            namespace TestApp;

            public class Processor
            {
                public string Run(string raw)
                {
                    var clean = StringHelper.Sanitize(raw);
                    return StringHelper.Sanitize(clean);
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.StringHelper.Sanitize", "method");

            var options = new SymbolRenameOptions(
                RenameOverloads: false,
                RenameInStrings: false,
                RenameInComments: false,
                RenameFile: false);

            var newSln = await Renamer.RenameSymbolAsync(sln, sym, options, "Clean");
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var utilContent = await project.ReadSourceAsync("Util.cs");
        Assert.Contains("public static string Clean(", utilContent);

        var consumerContent = await project.ReadSourceAsync("Consumer.cs");
        Assert.Contains("StringHelper.Clean(", consumerContent);
        Assert.DoesNotContain("Sanitize", consumerContent);
    }

    [Fact]
    public async Task Rename_DryRun_Does_Not_Modify_Files()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Original.cs", """
            namespace TestApp;

            public class Foo
            {
                public int Value { get; set; }
            }
            """);

        var slnPath = await project.BuildAsync();
        var originalContent = await project.ReadSourceAsync("Original.cs");

        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.Foo", "type");

            var options = new SymbolRenameOptions(
                RenameOverloads: false,
                RenameInStrings: false,
                RenameInComments: false,
                RenameFile: false);

            var newSln = await Renamer.RenameSymbolAsync(sln, sym, options, "Bar");
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: true);
        }

        var afterContent = await project.ReadSourceAsync("Original.cs");
        Assert.Equal(originalContent, afterContent);
    }
}
