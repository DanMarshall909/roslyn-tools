using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

/// <summary>
///     US-135: <c>rt inspect outline</c> defaults to a compact per-type signature array
///     (<see cref="OutlineTypeResult" /> with only <c>Name</c>, <c>Kind</c>, <c>Signatures</c>
///     populated); <c>--with-locations</c> populates the verbose fields of the same record;
///     <c>--summary</c> is unchanged and still wins (returns a <see cref="Dictionary{TKey,TValue}" />).
/// </summary>
public class OutlineCompact
{
    private static AdhocProjectBuilder Fixture()
    {
        var project = new AdhocProjectBuilder();
        project.AddSource("Order.cs", """
                                      namespace TestApp;

                                      public class Order
                                      {
                                          public int Id { get; set; }
                                          public void Cancel() { }
                                      }
                                      """);
        return project;
    }

    [Fact]
    public async Task default_returns_compact_signature_array_per_type()
    {
        using var project = Fixture();
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await new OutlineService()
                .OutlineFileAsync(sln, Path.Combine(project.ProjectDir, "Order.cs"));

            var order = result.Cast<OutlineTypeResult>().Single(t => t.Name == "Order");
            order.Kind.ShouldBe("Class");
            order.Signatures.ShouldNotBeNull();
            order.Signatures.ShouldContain("void Cancel()");
            order.Signatures.ShouldContain(s => s.Contains("Id"));
            // Compact path leaves verbose-only fields null (driver of the omitted JSON keys).
            order.Fqn.ShouldBeNull();
            order.Accessibility.ShouldBeNull();
            order.LineFrom.ShouldBeNull();
            order.LineTo.ShouldBeNull();
            order.Members.ShouldBeNull();
        }
    }

    [Fact]
    public async Task with_locations_restores_legacy_verbose_shape()
    {
        using var project = Fixture();
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await new OutlineService()
                .OutlineFileAsync(sln, Path.Combine(project.ProjectDir, "Order.cs"), withLocations: true);

            var order = result.Cast<OutlineTypeResult>().Single(t => t.Name == "Order");
            order.LineFrom.ShouldNotBeNull();
            order.Members.ShouldNotBeNull();
            // Verbose path populates the full record except Signatures (driver of the omitted JSON key).
            order.Fqn.ShouldNotBeNull();
            order.Accessibility.ShouldNotBeNull();
            order.Signatures.ShouldBeNull();
        }
    }

    [Fact]
    public async Task summary_takes_precedence_over_with_locations()
    {
        using var project = Fixture();
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await new OutlineService()
                .OutlineFileAsync(sln, Path.Combine(project.ProjectDir, "Order.cs"),
                    summary: true, withLocations: true);

            var order = result.Cast<Dictionary<string, object?>>().Single();
            order.ShouldNotContainKey("members");
            order.ShouldContainKey("fqn");
        }
    }
}
