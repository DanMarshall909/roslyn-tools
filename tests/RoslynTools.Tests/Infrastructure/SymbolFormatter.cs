using Microsoft.CodeAnalysis;
using RoslynTools.Core;

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
        return Assert.IsAssignableFrom<INamedTypeSymbol>(sym);
    }

    [Fact]
    public async Task returns_the_file_path_and_line_number()
    {
        var type = await LoadCircleSymbolAsync();

        var locations = Core.SymbolFormatter.ExtractSourceLocations(type);

        Assert.NotEmpty(locations);
        var first = locations[0];
        Assert.True(first.ContainsKey("file"), "Expected a 'file' key in the location entry.");
        Assert.True(first.ContainsKey("line"), "Expected a 'line' key in the location entry.");
        Assert.IsType<int>(first["line"]);
        Assert.True((int)first["line"]! >= 1, "Line number should be 1-based.");
    }

    [Fact]
    public async Task includes_static_flags_when_requested()
    {
        var type = await LoadCircleSymbolAsync();

        var members = Core.SymbolFormatter.ExtractMembers(type, true);

        Assert.NotEmpty(members);
        Assert.All(members,
            m => Assert.True(m.ContainsKey("isStatic"), "Expected 'isStatic' key when includeStaticFlag=true."));

        // The static factory method Unit() should be present and marked isStatic=true
        var unit = members.FirstOrDefault(m => m["name"] is "Unit");
        Assert.NotNull(unit);
        Assert.True((bool)unit["isStatic"]!, "Unit() should be flagged as static.");
    }

    [Fact]
    public async Task omits_static_flags_when_disabled()
    {
        var type = await LoadCircleSymbolAsync();

        var members = Core.SymbolFormatter.ExtractMembers(type, false);

        Assert.NotEmpty(members);
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
        var baseType = Assert.IsType<string>(result["baseType"]);
        Assert.Contains("BaseShape", baseType);

        Assert.True(result.ContainsKey("interfaces"), "Expected 'interfaces' key for Circle (implements IShape).");
        var interfaces = Assert.IsAssignableFrom<IEnumerable<string>>(result["interfaces"]);
        Assert.Contains(interfaces, i => i.Contains("IShape"));
    }

    [Fact]
    public async Task omits_the_object_base_type()
    {
        // BaseShape extends System.Object directly — baseType should be omitted.
        _project.AddSource("Shapes.cs", SharedSource);
        var (workspace, sln) = _project.Build();
        var sym = await Core.SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.BaseShape", "type");
        var baseShapeType = Assert.IsAssignableFrom<INamedTypeSymbol>(sym);

        var result = new Dictionary<string, object?>();
        Core.SymbolFormatter.AddTypeHierarchyInfo(result, baseShapeType);

        Assert.False(result.ContainsKey("baseType"), "baseType should be omitted when the base is System.Object.");
    }

    [Fact]
    public async Task no_attributes_omits_attributes_key()
    {
        var type = await LoadCircleSymbolAsync();

        var members = Core.SymbolFormatter.ExtractMembers(type, false);

        Assert.NotEmpty(members);
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
        var type = Assert.IsAssignableFrom<INamedTypeSymbol>(sym);

        var members = Core.SymbolFormatter.ExtractMembers(type, false);

        var plain = members.Single(m => m["name"] is "plain_fact");
        var attrs = Assert.IsAssignableFrom<List<string>>(plain["attributes"]);
        Assert.Single(attrs);
        Assert.Equal("Fact", attrs[0]);

        var skipped = members.Single(m => m["name"] is "skipped_fact");
        var skippedAttrs = Assert.IsAssignableFrom<List<string>>(skipped["attributes"]);
        Assert.Equal(2, skippedAttrs.Count);
        Assert.Contains(skippedAttrs, a => a.StartsWith("Fact(Skip = ", StringComparison.Ordinal));
        Assert.Contains(skippedAttrs, a => a == "Trait(\"Concern\", \"EdgeCase\")");
    }
}
