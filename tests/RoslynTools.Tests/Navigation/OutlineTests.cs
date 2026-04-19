using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Tests.Navigation;

public class OutlineTests
{
    [Fact]
    public async Task Lists_all_types_in_the_file()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Domain.cs", """
            namespace TestApp.Domain;

            public class Order
            {
                public int Id { get; set; }
                public string CustomerName { get; set; } = "";
                public decimal Total { get; set; }
                public void Cancel() { }
            }

            public enum OrderStatus
            {
                Pending,
                Confirmed,
                Cancelled
            }

            public interface IOrderRepository
            {
                Order? GetById(int id);
                void Save(Order order);
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var types = await GetTypesInFileAsync(sln, Path.Combine(project.ProjectDir, "Domain.cs"));

            var typeNames = types.Select(t => t.Name).ToList();
            Assert.Contains("Order", typeNames);
            Assert.Contains("OrderStatus", typeNames);
            Assert.Contains("IOrderRepository", typeNames);
            Assert.Equal(3, typeNames.Count);
        }
    }

    [Fact]
    public async Task Includes_members_for_each_type()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
            namespace TestApp;

            public class UserService
            {
                private readonly string _connectionString;

                public UserService(string connectionString)
                {
                    _connectionString = connectionString;
                }

                public string GetUser(int id) => "";
                public void DeleteUser(int id) { }
                public int Count => 0;
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var types = await GetTypesInFileAsync(sln, Path.Combine(project.ProjectDir, "Service.cs"));
            var userService = types.Single(t => t.Name == "UserService");

            var memberNames = userService.GetMembers()
                .Where(m => !m.IsImplicitlyDeclared)
                .Select(m => m.Name)
                .ToList();

            Assert.Contains("_connectionString", memberNames);
            Assert.Contains("GetUser", memberNames);
            Assert.Contains("DeleteUser", memberNames);
            Assert.Contains("Count", memberNames);
            Assert.Contains(".ctor", memberNames); // constructor
        }
    }

    [Fact]
    public async Task Returns_types_across_files()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("FileA.cs", """
            namespace TestApp.Models;

            public class Product
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }
            """);
        project.AddSource("FileB.cs", """
            namespace TestApp.Models;

            public class Category
            {
                public int Id { get; set; }
                public string Title { get; set; } = "";
            }
            """);
        project.AddSource("Other.cs", """
            namespace TestApp.Services;

            public class ProductService { }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var types = await GetTypesInNamespaceAsync(sln, "TestApp.Models");
            var typeNames = types.Select(t => t.Name).ToList();

            Assert.Contains("Product", typeNames);
            Assert.Contains("Category", typeNames);
            Assert.DoesNotContain("ProductService", typeNames);
            Assert.Equal(2, typeNames.Count);
        }
    }

    // Helper: find all types declared in a file via semantic model
    private static async Task<List<INamedTypeSymbol>> GetTypesInFileAsync(Solution solution, string filePath)
    {
        var results = new List<INamedTypeSymbol>();
        foreach (var proj in solution.Projects)
        {
            foreach (var doc in proj.Documents)
            {
                if (!string.Equals(doc.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                var model = await doc.GetSemanticModelAsync();
                var root = await doc.GetSyntaxRootAsync();
                if (model == null || root == null) continue;

                var types = root.DescendantNodes()
                    .Select(n => model.GetDeclaredSymbol(n))
                    .OfType<INamedTypeSymbol>()
                    .Distinct(SymbolEqualityComparer.Default)
                    .Cast<INamedTypeSymbol>()
                    .ToList();

                results.AddRange(types);
            }
        }
        return results;
    }

    // Helper: find all types in a namespace via compilation
    private static async Task<List<INamedTypeSymbol>> GetTypesInNamespaceAsync(Solution solution, string namespaceName)
    {
        var results = new List<INamedTypeSymbol>();
        var seen = new HashSet<string>();

        foreach (var proj in solution.Projects)
        {
            var compilation = await proj.GetCompilationAsync();
            if (compilation == null) continue;

            CollectTypesInNamespace(compilation.GlobalNamespace, namespaceName, results, seen);
        }
        return results;
    }

    private static void CollectTypesInNamespace(
        INamespaceSymbol ns, string target,
        List<INamedTypeSymbol> results, HashSet<string> seen)
    {
        if (ns.ToDisplayString() == target)
        {
            foreach (var type in ns.GetTypeMembers())
            {
                var fqn = type.ToDisplayString();
                if (seen.Add(fqn))
                    results.Add(type);
            }
        }
        foreach (var child in ns.GetNamespaceMembers())
            CollectTypesInNamespace(child, target, results, seen);
    }
}
