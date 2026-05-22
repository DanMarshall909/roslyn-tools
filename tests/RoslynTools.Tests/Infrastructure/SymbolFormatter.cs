using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public class SymbolFormatter : IDisposable
{
    // Shared source used by most tests: a class that extends a base and implements an interface.
    private const string SharedSource = """
                                        namespace TestApp;

                                        public interface IShape
                                        {
                                            double Area();
                                        }

                                        public abstract class BaseShape
                                        {
                                            public string Color { get; set; } = "red";
                                        }

                                        public class Circle : BaseShape, IShape
                                        {
                                            public double Radius { get; set; }

                                            public double Area() => Math.PI * Radius * Radius;

                                            public static Circle Unit() => new() { Radius = 1.0 };
                                        }
                                        """;

    private readonly AdhocProjectBuilder _project = new();

    public void Dispose()
    {
        _project.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<INamedTypeSymbol> LoadCircleSymbolAsync()
    {
        _project.AddSource("Shapes.cs", SharedSource);
        var (workspace, sln) = _project.Build();
        // Workspace is intentionally not disposed here; caller is responsible via the IDisposable chain.
        // In these tests we rely on GC / test teardown — acceptable for integration tests against throwaway dirs.
        var sym = await Core.SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.Circle", "type");
        return sym.ShouldBeAssignableTo<INamedTypeSymbol>()!;
    }

    [Fact]
    public async Task returns_the_file_path_and_line_number()
    {
        var type = await LoadCircleSymbolAsync();

        var locations = Core.SymbolFormatter.ExtractSourceLocations(type);

        locations.ShouldNotBeEmpty();
        var first = locations[0];
        Assert.True(first.ContainsKey("file"), "Expected a 'file' key in the location entry.");
        Assert.True(first.ContainsKey("line"), "Expected a 'line' key in the location entry.");
        first["line"].ShouldBeOfType<int>();
        Assert.True((int)first["line"]! >= 1, "Line number should be 1-based.");
    }

    [Fact]
    public async Task includes_static_flags_when_requested()
    {
        var type = await LoadCircleSymbolAsync();

        var members = Core.SymbolFormatter.ExtractMembers(type, true);

        members.ShouldNotBeEmpty();
        Assert.All(members,
            m => Assert.True(m.ContainsKey("isStatic"), "Expected 'isStatic' key when includeStaticFlag=true."));

        // The static factory method Unit() should be present and marked isStatic=true
        var unit = members.FirstOrDefault(m => m["name"] is "Unit");
        unit.ShouldNotBeNull();
        Assert.True((bool)unit["isStatic"]!, "Unit() should be flagged as static.");
    }

    [Fact]
    public async Task omits_static_flags_when_disabled()
    {
        var type = await LoadCircleSymbolAsync();

        var members = Core.SymbolFormatter.ExtractMembers(type, false);

        members.ShouldNotBeEmpty();
        Assert.All(members,
            m => Assert.False(m.ContainsKey("isStatic"), "Expected no 'isStatic' key when includeStaticFlag=false."));
    }

    [Fact]
    public async Task adds_the_base_type_and_interfaces()
    {
        var type = await LoadCircleSymbolAsync();
        var result = new Dictionary<string, object?>();

        Core.SymbolFormatter.AddTypeHierarchyInfo(result, type);

        Assert.True(result.ContainsKey("baseType"), "Expected 'baseType' key for Circle (extends BaseShape).");
        var baseType = result["baseType"].ShouldBeOfType<string>();
        baseType.ShouldContain("BaseShape", Case.Sensitive);

        Assert.True(result.ContainsKey("interfaces"), "Expected 'interfaces' key for Circle (implements IShape).");
        var interfaces = result["interfaces"].ShouldBeAssignableTo<IEnumerable<string>>()!;
        interfaces.ShouldContain(i => i.Contains("IShape"));
    }

    [Fact]
    public async Task omits_the_object_base_type()
    {
        // BaseShape extends System.Object directly — baseType should be omitted.
        _project.AddSource("Shapes.cs", SharedSource);
        var (workspace, sln) = _project.Build();
        var sym = await Core.SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.BaseShape", "type");
        var baseShapeType = sym.ShouldBeAssignableTo<INamedTypeSymbol>()!;

        var result = new Dictionary<string, object?>();
        Core.SymbolFormatter.AddTypeHierarchyInfo(result, baseShapeType);

        Assert.False(result.ContainsKey("baseType"), "baseType should be omitted when the base is System.Object.");
    }

    [Fact]
    public async Task no_attributes_omits_attributes_key()
    {
        var type = await LoadCircleSymbolAsync();

        var members = Core.SymbolFormatter.ExtractMembers(type, false);

        members.ShouldNotBeEmpty();
        Assert.All(members, m => Assert.False(m.ContainsKey("attributes"),
            $"Member '{m["name"]}' has no attributes but got an 'attributes' key."));
    }

    [Fact]
    public async Task attributes_includes_formatted_attribute_list()
    {
        _project.AddSource("Tests.cs", """
                                       namespace TestApp;

                                       [System.AttributeUsage(System.AttributeTargets.Method)]
                                       public class FactAttribute : System.Attribute
                                       {
                                           public string Skip { get; set; } = "";
                                       }

                                       [System.AttributeUsage(System.AttributeTargets.Method)]
                                       public class TraitAttribute : System.Attribute
                                       {
                                           public TraitAttribute(string name, string value) { }
                                       }

                                       public class MyTests
                                       {
                                           [Fact]
                                           public void plain_fact() { }

                                           [Fact(Skip = "pending")]
                                           [Trait("Concern", "EdgeCase")]
                                           public void skipped_fact() { }
                                       }
                                       """);
        var (workspace, sln) = _project.Build();
        var sym = await Core.SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.MyTests", "type");
        var type = sym.ShouldBeAssignableTo<INamedTypeSymbol>()!;

        var members = Core.SymbolFormatter.ExtractMembers(type, false);

        var plain = members.Single(m => m["name"] is "plain_fact");
        var attrs = plain["attributes"].ShouldBeAssignableTo<List<string>>()!;
        Assert.Single(attrs);
        attrs[0].ShouldBe("Fact");

        var skipped = members.Single(m => m["name"] is "skipped_fact");
        var skippedAttrs = skipped["attributes"].ShouldBeAssignableTo<List<string>>()!;
        skippedAttrs.Count.ShouldBe(2);
        skippedAttrs.ShouldContain(a => a.StartsWith("Fact(Skip = ", StringComparison.Ordinal));
        skippedAttrs.ShouldContain(a => a == "Trait(\"Concern\", \"EdgeCase\")");
    }
}
