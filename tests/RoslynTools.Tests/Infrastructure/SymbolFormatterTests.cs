using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Tests.Infrastructure;

public class SymbolFormatterTests : IDisposable
{
    private readonly AdhocProjectBuilder _project = new();

    public void Dispose()
    {
        _project.Dispose();
        GC.SuppressFinalize(this);
    }

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

    private async Task<INamedTypeSymbol> LoadCircleSymbolAsync()
    {
        _project.AddSource("Shapes.cs", SharedSource);
        var (workspace, sln) = _project.Build();
        // Workspace is intentionally not disposed here; caller is responsible via the IDisposable chain.
        // In these tests we rely on GC / test teardown — acceptable for integration tests against throwaway dirs.
        var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.Circle", "type");
        return Assert.IsAssignableFrom<INamedTypeSymbol>(sym);
    }

    [Fact]
    public async Task ExtractSourceLocations_ReturnsFileAndLine()
    {
        var type = await LoadCircleSymbolAsync();

        var locations = SymbolFormatter.ExtractSourceLocations(type);

        Assert.NotEmpty(locations);
        var first = locations[0];
        Assert.True(first.ContainsKey("file"), "Expected a 'file' key in the location entry.");
        Assert.True(first.ContainsKey("line"), "Expected a 'line' key in the location entry.");
        Assert.IsType<int>(first["line"]);
        Assert.True((int)first["line"]! >= 1, "Line number should be 1-based.");
    }

    [Fact]
    public async Task ExtractMembers_IncludesStaticFlag()
    {
        var type = await LoadCircleSymbolAsync();

        var members = SymbolFormatter.ExtractMembers(type, includeStaticFlag: true);

        Assert.NotEmpty(members);
        Assert.All(members, m => Assert.True(m.ContainsKey("isStatic"), "Expected 'isStatic' key when includeStaticFlag=true."));

        // The static factory method Unit() should be present and marked isStatic=true
        var unit = members.FirstOrDefault(m => m["name"] is "Unit");
        Assert.NotNull(unit);
        Assert.True((bool)unit["isStatic"]!, "Unit() should be flagged as static.");
    }

    [Fact]
    public async Task ExtractMembers_OmitsStaticFlagWhenDisabled()
    {
        var type = await LoadCircleSymbolAsync();

        var members = SymbolFormatter.ExtractMembers(type, includeStaticFlag: false);

        Assert.NotEmpty(members);
        Assert.All(members, m => Assert.False(m.ContainsKey("isStatic"), "Expected no 'isStatic' key when includeStaticFlag=false."));
    }

    [Fact]
    public async Task AddTypeHierarchyInfo_SetsBaseTypeAndInterfaces()
    {
        var type = await LoadCircleSymbolAsync();
        var result = new Dictionary<string, object?>();

        SymbolFormatter.AddTypeHierarchyInfo(result, type);

        Assert.True(result.ContainsKey("baseType"), "Expected 'baseType' key for Circle (extends BaseShape).");
        var baseType = Assert.IsType<string>(result["baseType"]);
        Assert.Contains("BaseShape", baseType);

        Assert.True(result.ContainsKey("interfaces"), "Expected 'interfaces' key for Circle (implements IShape).");
        var interfaces = Assert.IsAssignableFrom<IEnumerable<string>>(result["interfaces"]);
        Assert.Contains(interfaces, i => i.Contains("IShape"));
    }

    [Fact]
    public async Task AddTypeHierarchyInfo_OmitsObjectBase()
    {
        // BaseShape extends System.Object directly — baseType should be omitted.
        _project.AddSource("Shapes.cs", SharedSource);
        var (workspace, sln) = _project.Build();
        var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, "TestApp.BaseShape", "type");
        var baseShapeType = Assert.IsAssignableFrom<INamedTypeSymbol>(sym);

        var result = new Dictionary<string, object?>();
        SymbolFormatter.AddTypeHierarchyInfo(result, baseShapeType);

        Assert.False(result.ContainsKey("baseType"), "baseType should be omitted when the base is System.Object.");
    }
}
