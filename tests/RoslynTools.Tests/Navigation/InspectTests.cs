using RoslynTools.Core;

namespace RoslynTools.Tests.Navigation;

public class InspectTests
{
    [Fact]
    public async Task Returns_its_members_and_base_type()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Models.cs", """
            namespace TestApp;

            public abstract class BaseEntity
            {
                public int Id { get; set; }
            }

            public class Customer : BaseEntity
            {
                public string Name { get; set; } = "";
                public string Email { get; set; } = "";
                public bool IsActive { get; set; }

                public void Activate() { IsActive = true; }
                public static Customer Create(string name) => new() { Name = name };
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.Customer", "type");
            Assert.Equal("Customer", sym.Name);

            // Verify it's a named type with expected members
            var namedType = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.INamedTypeSymbol>(sym);
            Assert.Equal("BaseEntity", namedType.BaseType?.Name);

            var memberNames = namedType.GetMembers()
                .Where(m => !m.IsImplicitlyDeclared)
                .Select(m => m.Name)
                .ToList();

            Assert.Contains("Name", memberNames);
            Assert.Contains("Email", memberNames);
            Assert.Contains("IsActive", memberNames);
            Assert.Contains("Activate", memberNames);
            Assert.Contains("Create", memberNames);
        }
    }

    [Fact]
    public async Task Returns_its_interfaces()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services.cs", """
            namespace TestApp;

            public interface IService
            {
                void Execute();
            }

            public interface ILoggable
            {
                void Log(string message);
            }

            public class MyService : IService, ILoggable
            {
                public void Execute() { }
                public void Log(string message) { }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.MyService", "type");
            var namedType = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.INamedTypeSymbol>(sym);

            var ifaceNames = namedType.Interfaces.Select(i => i.Name).ToList();
            Assert.Contains("IService", ifaceNames);
            Assert.Contains("ILoggable", ifaceNames);
        }
    }

    [Fact]
    public async Task Returns_its_parameters_and_return_type()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Calculator.cs", """
            namespace TestApp;

            public class Calculator
            {
                public int Add(int a, int b) => a + b;
                public double Divide(double numerator, double denominator) => numerator / denominator;
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.Calculator.Add", "method");
            var method = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.IMethodSymbol>(sym);

            Assert.Equal("int", method.ReturnType.ToDisplayString());
            Assert.Equal(2, method.Parameters.Length);
            Assert.Equal("a", method.Parameters[0].Name);
            Assert.Equal("b", method.Parameters[1].Name);
        }
    }

    [Fact]
    public async Task Returns_its_type_and_accessors()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Config.cs", """
            namespace TestApp;

            public class Config
            {
                public string Name { get; set; } = "";
                public int MaxRetries { get; }
                public bool IsEnabled { set { } }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.Config.Name", "property");
            var prop = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.IPropertySymbol>(sym);
            Assert.NotNull(prop.GetMethod);
            Assert.NotNull(prop.SetMethod);

            var readOnly = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.Config.MaxRetries", "property");
            var readOnlyProp = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.IPropertySymbol>(readOnly);
            Assert.NotNull(readOnlyProp.GetMethod);
            Assert.Null(readOnlyProp.SetMethod);
        }
    }
}
