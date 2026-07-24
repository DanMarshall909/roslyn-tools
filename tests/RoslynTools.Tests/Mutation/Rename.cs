using Microsoft.CodeAnalysis.Rename;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Models;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class Rename : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public Rename(RtServicesFixture services) => _services = services;

    [Fact]
    public void build_exposes_new_name_option_for_piped_symbol_usage()
    {
        var command = _services.Resolve<RenameCommand>().Build();

        command.Options.ShouldContain(opt => opt.Name == "new-name");
    }

    [Fact]
    public async Task updates_the_declaration_and_references()
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
            var sym = await RoslynTools.Core.SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.Product", "type");

            var options = new SymbolRenameOptions(
                false,
                false,
                false,
                false);

            var newSln = await Renamer.RenameSymbolAsync(sln, sym, options, "Item");

            await RoslynTools.Core.SolutionWriter.ApplyChangesAsync(sln, newSln, false);
        }

        var modelContent = await project.ReadSourceAsync("Model.cs");
        modelContent.ShouldContain("public class Item", Case.Sensitive);
        modelContent.ShouldNotContain("class Product", Case.Sensitive);

        var serviceContent = await project.ReadSourceAsync("Service.cs");
        serviceContent.ShouldContain("Item", Case.Sensitive);
        serviceContent.ShouldNotContain("new Product()", Case.Sensitive);
        serviceContent.ShouldContain("new Item()", Case.Sensitive);
    }

    [Fact]
    public async Task updates_all_call_sites()
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
            var sym = await RoslynTools.Core.SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.StringHelper.Sanitize", "method");

            var options = new SymbolRenameOptions(
                false,
                false,
                false,
                false);

            var newSln = await Renamer.RenameSymbolAsync(sln, sym, options, "Clean");
            await RoslynTools.Core.SolutionWriter.ApplyChangesAsync(sln, newSln, false);
        }

        var utilContent = await project.ReadSourceAsync("Util.cs");
        utilContent.ShouldContain("public static string Clean(", Case.Sensitive);

        var consumerContent = await project.ReadSourceAsync("Consumer.cs");
        consumerContent.ShouldContain("StringHelper.Clean(", Case.Sensitive);
        consumerContent.ShouldNotContain("Sanitize", Case.Sensitive);
    }

    [Fact]
    public async Task does_not_modify_files()
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
            var sym = await RoslynTools.Core.SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.Foo", "type");

            var options = new SymbolRenameOptions(
                false,
                false,
                false,
                false);

            var newSln = await Renamer.RenameSymbolAsync(sln, sym, options, "Bar");
            await RoslynTools.Core.SolutionWriter.ApplyChangesAsync(sln, newSln, true);
        }

        var afterContent = await project.ReadSourceAsync("Original.cs");
        afterContent.ShouldBe(originalContent);
    }

    [Fact]
    public async Task dry_run_sends_the_renamed_solution_to_the_preview_writer()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", "namespace TestApp; public class Widget { }");
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var writer = new CapturingSolutionChangeWriter();
            var service = new RenameService(NullLogger<RenameService>.Instance, writer);

            var result = await service.ExecuteAsync(
                solution,
                new RenameRequest(
                    "TestApp.Widget",
                    "Gadget",
                    "type",
                    true,
                    new HashSet<string>(StringComparer.Ordinal)));

            writer.DryRun.ShouldBe(true);
            writer.NewSolution.ShouldNotBeNull();
            result.Applied.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task renames_csv_batch_updates_multiple_symbols()
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
        modelContent.ShouldContain("class Item", Case.Sensitive);

        var serviceContent = await project.ReadSourceAsync("Service.cs");
        serviceContent.ShouldContain("Item FetchItem()", Case.Sensitive);
        serviceContent.ShouldContain("new Item()", Case.Sensitive);
    }

    [Fact]
    public async Task renames_csv_batch_rejects_flagged_rows()
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
                _services.Resolve<RenameCommand>().ExecuteBatchAsync(sln, csvPath, true));

            ex.Message.ShouldContain("review column", Case.Sensitive);
        }
    }
}
