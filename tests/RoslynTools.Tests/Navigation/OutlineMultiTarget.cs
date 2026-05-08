using RoslynTools.Services;

namespace RoslynTools.Tests.Navigation;

public class OutlineMultiTarget
{
    [Fact]
    public async Task outlines_nested_types_when_file_uses_unix_style_separators_in_argv()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Container.cs", """
                                          namespace TestApp;
                                          public class Container
                                          {
                                              public class NestedHelper { }
                                              public class NestedProcessor { }
                                          }
                                          """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var svc = new OutlineService();
            var path = Path.Combine(project.ProjectDir, "Container.cs").Replace('\\', '/');
            var result = await svc.OutlineFileAsync(sln, path);
            var names = result.Select(r => (string)r["name"]!).ToList();
            Assert.Contains("Container", names);
            Assert.Contains("NestedHelper", names);
            Assert.Contains("NestedProcessor", names);
        }
    }

    [Fact]
    public async Task returns_empty_list_for_file_not_in_solution()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Real.cs", "namespace Foo; public class Real { }");

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var svc = new OutlineService();
            var missingPath = Path.Combine(project.ProjectDir, "DoesNotExist.cs");
            var result = await svc.OutlineFileAsync(sln, missingPath);
            Assert.Empty(result);
        }
    }

    [Fact]
    public async Task returns_empty_list_for_typeless_file()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Program.cs", """
                                        Console.WriteLine("Hello");
                                        """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var svc = new OutlineService();
            var result = await svc.OutlineFileAsync(sln, Path.Combine(project.ProjectDir, "Program.cs"));
            Assert.Empty(result);
        }
    }

    [Fact]
    public async Task returns_types_from_all_matching_files_for_double_star_glob()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Models/Product.cs", """
                                               namespace TestApp.Models;
                                               public class Product { }
                                               """);
        project.AddSource("Services/OrderService.cs", """
                                                      namespace TestApp.Services;
                                                      public class OrderService { }
                                                      """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var svc = new OutlineService();
            var glob = Path.Combine(project.ProjectDir, "**", "*.cs");
            var result = await svc.OutlineGlobAsync(sln, glob);
            var typeNames = result.Select(r => (string)r["name"]!).ToList();
            Assert.Contains("Product", typeNames);
            Assert.Contains("OrderService", typeNames);
        }
    }

    [Fact]
    public async Task returns_only_that_dirs_types_for_single_dir_glob()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Models/Product.cs", """
                                               namespace TestApp.Models;
                                               public class Product { }
                                               """);
        project.AddSource("Services/OrderService.cs", """
                                                      namespace TestApp.Services;
                                                      public class OrderService { }
                                                      """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var svc = new OutlineService();
            var glob = Path.Combine(project.ProjectDir, "Models", "*.cs");
            var result = await svc.OutlineGlobAsync(sln, glob);
            var typeNames = result.Select(r => (string)r["name"]!).ToList();
            Assert.Contains("Product", typeNames);
            Assert.DoesNotContain("OrderService", typeNames);
        }
    }

    [Fact]
    public async Task returns_types_from_all_sub_namespaces_for_namespace_prefix()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Models/Product.cs", """
                                               namespace TestApp.Models;
                                               public class Product { }
                                               """);
        project.AddSource("Services/OrderService.cs", """
                                                      namespace TestApp.Services;
                                                      public class OrderService { }
                                                      """);
        project.AddSource("Unrelated.cs", """
                                          namespace Other;
                                          public class Unrelated { }
                                          """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var svc = new OutlineService();
            var result = await svc.OutlineNamespaceAsync(sln, "TestApp.*");
            var typeNames = result.Select(r => (string)r["name"]!).ToList();
            Assert.Contains("Product", typeNames);
            Assert.Contains("OrderService", typeNames);
            Assert.DoesNotContain("Unrelated", typeNames);
        }
    }

    [Fact]
    public async Task returns_types_from_matching_files_only_for_regex()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Models/Product.cs", """
                                               namespace TestApp.Models;
                                               public class Product { }
                                               """);
        project.AddSource("Services/OrderService.cs", """
                                                      namespace TestApp.Services;
                                                      public class OrderService { }
                                                      """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var svc = new OutlineService();
            var result = await svc.OutlineRegexAsync(sln, @"Service\.cs$");
            var typeNames = result.Select(r => (string)r["name"]!).ToList();
            Assert.Contains("OrderService", typeNames);
            Assert.DoesNotContain("Product", typeNames);
        }
    }
}
