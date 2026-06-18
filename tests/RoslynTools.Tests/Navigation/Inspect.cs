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
    public async Task describe_defaults_to_compact_signature_array_for_a_type()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public sealed class Widget
                                       {
                                           public string Label { get; set; } = "";
                                           public void Activate() { }
                                       }
                                       """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await Service().Describe(sln, "TestApp.Widget", "type");

            result.IsOk.ShouldBeTrue();
            var compact = result.Match(v => v, _ => (object)null!).ShouldBeOfType<CompactDescription>();
            compact.Fqn.ShouldBe("TestApp.Widget");
            compact.Kind.ShouldBe("Class");
            compact.Signatures.ShouldContain("void Activate()");
            compact.Signatures.ShouldContain(s => s.Contains("Label"));
        }
    }

    [Fact]
    public async Task describe_compact_default_omits_line_numbers_and_locations()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public sealed class Widget { public int Id { get; set; } }
                                       """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await Service().Describe(sln, "TestApp.Widget", "type");

            var compact = result.Match(v => v, _ => (object)null!).ShouldBeOfType<CompactDescription>();
            compact.Signatures.ShouldNotContain(s => s.Contains("lineFrom"));
        }
    }

    [Fact]
    public async Task describe_with_locations_restores_legacy_verbose_shape()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public sealed class Widget { public string Label { get; set; } = ""; }
                                       """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await Service().Describe(sln, "TestApp.Widget", "type", withLocations: true);

            result.IsOk.ShouldBeTrue();
            var inspection = result.Match(v => v, _ => (object)null!).ShouldBeOfType<Dictionary<string, object?>>();
            inspection["name"].ShouldBe("Widget");
            inspection["fqn"].ShouldBe("TestApp.Widget");
            inspection.ShouldContainKey("locations");
            inspection.ShouldContainKey("members");
        }
    }

    [Fact]
    [Trait("Concern", "DrillIn")]
    public async Task describe_with_member_signature_returns_full_member_detail()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Calc.cs", """
                                     namespace TestApp;

                                     public class Calculator
                                     {
                                         public int Add(int a, int b) => a + b;
                                         public double Add(double a, double b) => a + b;
                                     }
                                     """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await Service().Describe(sln, "TestApp.Calculator.Add(int, int)", null);

            result.IsOk.ShouldBeTrue();
            var detail = result.Match(v => v, _ => (object)null!).ShouldBeOfType<Dictionary<string, object?>>();
            detail["name"].ShouldBe("Add");
            detail["returnType"].ShouldBe("int");
            var parameters = detail["parameters"].ShouldBeAssignableTo<IEnumerable<Dictionary<string, object?>>>()!;
            parameters.Count().ShouldBe(2);
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
            var result = await Service().Describe(sln, "TestApp.Missing", "type");

            result.IsErr.ShouldBeTrue();
            result.Match(_ => RtErrorKind.NotFound, e => e.Kind).ShouldBe(RtErrorKind.NotFound);
        }
    }

    private static InspectService Service() => new();
}
