using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public class Inspect
{
    [Fact]
    public async Task returns_its_members_and_base_type()
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
            sym.Name.ShouldBe("Customer");

            // Verify it's a named type with expected members
            var namedType = sym.ShouldBeAssignableTo<INamedTypeSymbol>()!;
            (namedType.BaseType?.Name).ShouldBe("BaseEntity");

            var memberNames = namedType.GetMembers()
                .Where(m => !m.IsImplicitlyDeclared)
                .Select(m => m.Name)
                .ToList();

            memberNames.ShouldContain("Name");
            memberNames.ShouldContain("Email");
            memberNames.ShouldContain("IsActive");
            memberNames.ShouldContain("Activate");
            memberNames.ShouldContain("Create");
        }
    }

    [Fact]
    public async Task returns_its_interfaces()
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
            var namedType = sym.ShouldBeAssignableTo<INamedTypeSymbol>()!;

            var ifaceNames = namedType.Interfaces.Select(i => i.Name).ToList();
            ifaceNames.ShouldContain("IService");
            ifaceNames.ShouldContain("ILoggable");
        }
    }

    [Fact]
    public async Task returns_its_parameters_and_return_type()
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
            var method = sym.ShouldBeAssignableTo<IMethodSymbol>()!;

            method.ReturnType.ToDisplayString().ShouldBe("int");
            method.Parameters.Length.ShouldBe(2);
            method.Parameters[0].Name.ShouldBe("a");
            method.Parameters[1].Name.ShouldBe("b");
        }
    }

    [Fact]
    public async Task returns_its_type_and_accessors()
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
            var prop = sym.ShouldBeAssignableTo<IPropertySymbol>()!;
            prop.GetMethod.ShouldNotBeNull();
            prop.SetMethod.ShouldNotBeNull();

            var readOnly = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.Config.MaxRetries", "property");
            var readOnlyProp = readOnly.ShouldBeAssignableTo<IPropertySymbol>()!;
            readOnlyProp.GetMethod.ShouldNotBeNull();
            readOnlyProp.SetMethod.ShouldBeNull();
        }
    }

    [Fact]
    public async Task describe_returns_ok_inspection_for_a_known_type()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public sealed class Widget
                                       {
                                           public string Label { get; set; } = "";
                                       }
                                       """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await new InspectService().Describe(sln, "TestApp.Widget", "type");

            result.IsOk.ShouldBeTrue();
            var inspection = result.Match(d => d, _ => new Dictionary<string, object?>());
            inspection["name"].ShouldBe("Widget");
            inspection["kind"].ShouldBe("NamedType");
            inspection["fqn"].ShouldBe("TestApp.Widget");
        }
    }

    [Fact]
    public async Task describe_returns_err_not_found_for_an_unknown_symbol()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public sealed class Widget
                                       {
                                       }
                                       """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await new InspectService().Describe(sln, "TestApp.Missing", "type");

            result.IsErr.ShouldBeTrue();
            result.Match(_ => RtErrorKind.NotFound, e => e.Kind).ShouldBe(RtErrorKind.NotFound);
        }
    }
}
