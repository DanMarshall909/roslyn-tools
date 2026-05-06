using Microsoft.CodeAnalysis.Rename;
using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public class RenameTests
{
    [Fact]
    public async Task Updates_the_declaration_and_references()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.Product", "type");

            var options = new SymbolRenameOptions(
                RenameOverloads: false,
                RenameInStrings: false,
                RenameInComments: false,
                RenameFile: false);

            var newSln = await Renamer.RenameSymbolAsync(sln, sym, options, "Item");

            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var modelContent = await project.ReadSourceAsync("Model.cs");
        Assert.Contains("public class Item", modelContent);
        Assert.DoesNotContain("class Product", modelContent);

        var serviceContent = await project.ReadSourceAsync("Service.cs");
        Assert.Contains("Item", serviceContent);
        Assert.DoesNotContain("new Product()", serviceContent);
        Assert.Contains("new Item()", serviceContent);
    }

    [Fact]
    public async Task Updates_all_call_sites()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Does_not_modify_files()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Original.cs", """
            namespace TestApp;

            public class Foo
            {
                public int Value { get; set; }
            }
            """);

        var (workspace, sln) = project.Build();
        var originalContent = await project.ReadSourceAsync("Original.cs");

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

    [Fact]
    public async Task Rename_Csv_Batch_Updates_Multiple_Symbols()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Model.cs", """
            namespace TestApp;

            public class Product
            {
                public int Id { get; set; }
            }
            """);
        project.AddSource("Service.cs", """
            namespace TestApp;

            public class ProductService
            {
                public Product GetProduct()
                {
                    return new Product();
                }
            }
            """);

        var (workspace, sln) = project.Build();
        var csvPath = Path.Combine(project.ProjectDir, "renames.csv");
        await File.WriteAllTextAsync(csvPath, """
            "source_symbol","target_name"
            "TestApp.Product","Item"
            "TestApp.ProductService.GetProduct","FetchItem"
            """);
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var originalError = Console.Error;
            using var errorWriter = new StringWriter();
            Console.SetError(errorWriter);
            try
            {
                var exitCode = await App.RunAsync(["rename", "--csv", csvPath, "--solution", "ignored"]);
                Assert.True(exitCode == 0, $"Expected exit code 0 but got {exitCode}. stderr: {errorWriter}");
            }
            finally
            {
                Console.SetError(originalError);
            }
        }

        var modelContent = await project.ReadSourceAsync("Model.cs");
        Assert.Contains("class Item", modelContent);

        var serviceContent = await project.ReadSourceAsync("Service.cs");
        Assert.Contains("Item FetchItem()", serviceContent);
        Assert.Contains("new Item()", serviceContent);
    }

    [Fact]
    public async Task Rename_Csv_Batch_Rejects_Flagged_Rows()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Model.cs", """
            namespace TestApp;

            public class Product
            {
                public int Id { get; set; }
            }
            """);

        var (workspace, sln) = project.Build();
        var csvPath = Path.Combine(project.ProjectDir, "renames-with-review.csv");
        await File.WriteAllTextAsync(csvPath, """
            "source_symbol","target_name","review"
            "TestApp.Product","Item","no-verb"
            """);

        using (workspace)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RenameCommand.ExecuteBatchAsync(sln, csvPath, dryRun: true));

            Assert.Contains("review column", ex.Message);
        }
    }
}
