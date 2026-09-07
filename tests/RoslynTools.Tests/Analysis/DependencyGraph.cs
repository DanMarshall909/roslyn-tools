using System.Text.Json;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public class DependencyGraph : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public DependencyGraph(RtServicesFixture services) => _services = services;

    /// <summary>
    ///     Builds a project with three classes:
    ///     A depends on B
    ///     B has no dependencies
    ///     C depends on A and B
    /// </summary>
    private static AdhocProjectBuilder BuildGraphProject()
    {
        var proj = new AdhocProjectBuilder("GraphApp");
        proj.AddSource("Types.cs", """
                                   class A { B _b; }
                                   class B { }
                                   class C { A _a; B _b; }
                                   """);
        return proj;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  1. JSON output contains nodes for A, B, C
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task returns_nodes_in_json_output()
    {
        using var proj = BuildGraphProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var json = await _services.Resolve<DependencyGraphService>().BuildGraphAsync(sln, null, null, "json", null);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var nodes = root.GetProperty("nodes");
            var nodeNames = nodes.EnumerateArray().Select(n => n.GetString()).ToList();

            nodeNames.ShouldContain("A");
            nodeNames.ShouldContain("B");
            nodeNames.ShouldContain("C");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. JSON output contains edges A→B, C→A, C→B
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task returns_edges_in_json_output()
    {
        using var proj = BuildGraphProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var json = await _services.Resolve<DependencyGraphService>().BuildGraphAsync(sln, null, null, "json", null);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var edges = root.GetProperty("edges").EnumerateArray()
                .Select(e => (From: e.GetProperty("from").GetString()!, To: e.GetProperty("to").GetString()!))
                .ToList();

            edges.ShouldContain(e => e.From == "A" && e.To == "B");
            edges.ShouldContain(e => e.From == "C" && e.To == "A");
            edges.ShouldContain(e => e.From == "C" && e.To == "B");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  3. DOT output starts with "digraph" and contains "->"
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task returns_valid_dot_output()
    {
        using var proj = BuildGraphProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var dot = await _services.Resolve<DependencyGraphService>().BuildGraphAsync(sln, null, null, "dot", null);

            Assert.StartsWith("digraph", dot, StringComparison.OrdinalIgnoreCase);
            dot.ShouldContain("->", Case.Sensitive);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. --depth 1 --type A returns only A's direct dependencies (B), not C
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task honours_the_depth_limit()
    {
        using var proj = BuildGraphProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var json = await _services.Resolve<DependencyGraphService>().BuildGraphAsync(sln, "A", 1, "json", null);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var nodes = root.GetProperty("nodes").EnumerateArray()
                .Select(n => n.GetString())
                .ToList();

            // A and its direct dependency B should be present
            nodes.ShouldContain("A");
            nodes.ShouldContain("B");

            // C depends on A, but C itself is not reachable from A within depth 1
            nodes.ShouldNotContain("C");
        }
    }

    [Fact]
    public async Task targeted_graph_keeps_method_body_dependencies_within_the_depth_limit()
    {
        using var proj = new AdhocProjectBuilder("GraphApp");
        proj.AddSource("Types.cs", """
                                   class Root
                                   {
                                       void Use() { var direct = new Direct(); direct.Ping(); }
                                   }
                                   class Direct { public void Ping() { } }
                                   class Unrelated
                                   {
                                       void Use() { var hidden = new Hidden(); hidden.Ping(); }
                                   }
                                   class Hidden { public void Ping() { } }
                                   """);
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var json = await _services.Resolve<DependencyGraphService>().BuildGraphAsync(
                sln, "Root", 1, "json", null);

            using var doc = JsonDocument.Parse(json);
            var nodes = doc.RootElement.GetProperty("nodes").EnumerateArray()
                .Select(node => node.GetString())
                .ToList();
            var edges = doc.RootElement.GetProperty("edges").EnumerateArray()
                .Select(edge => (
                    From: edge.GetProperty("from").GetString(),
                    To: edge.GetProperty("to").GetString()))
                .ToList();

            nodes.ShouldBe(["Direct", "Root"], ignoreOrder: true);
            edges.ShouldContain(edge => edge.From == "Root" && edge.To == "Direct");
        }
    }

    [Fact]
    public async Task source_type_discovery_keeps_all_explicit_declaration_kinds()
    {
        using var proj = new AdhocProjectBuilder("GraphApp");
        proj.AddSource("Declarations.cs", """
                                          delegate void Work();
                                          enum Status { Ready }
                                          interface IMarker { }
                                          record Item;
                                          class Outer { public class Inner { } }
                                          """);
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var json = await _services.Resolve<DependencyGraphService>().BuildGraphAsync(
                sln, null, null, "json", null);

            using var doc = JsonDocument.Parse(json);
            var nodes = doc.RootElement.GetProperty("nodes").EnumerateArray()
                .Select(node => node.GetString())
                .ToList();

            nodes.ShouldContain("Work");
            nodes.ShouldContain("Status");
            nodes.ShouldContain("IMarker");
            nodes.ShouldContain("Item");
            nodes.ShouldContain("Outer");
            nodes.ShouldContain("Inner");
        }
    }

    // Bug: tasks/dogfood-gaps.md:22 — graph edges miss local-variable type uses
    [Fact]
    public async Task returns_local_variable_dependencies()
    {
        using var proj = new AdhocProjectBuilder("GraphApp");
        proj.AddSource("Types.cs", """
                                   class A
                                   {
                                       void Use() { var b = new B(); b.Ping(); }
                                   }
                                   class B { public void Ping() { } }
                                   """);
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var json = await _services.Resolve<DependencyGraphService>().BuildGraphAsync(
                sln, null, null, "json", null);

            using var doc = JsonDocument.Parse(json);
            var edges = doc.RootElement.GetProperty("edges").EnumerateArray()
                .Select(e => (From: e.GetProperty("from").GetString()!,
                    To: e.GetProperty("to").GetString()!))
                .ToList();

            edges.ShouldContain(e => e.From == "A" && e.To == "B");
        }
    }

    [Fact]
    public async Task returns_empty_graph_for_unknown_type_in_json()
    {
        using var proj = BuildGraphProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var json = await _services.Resolve<DependencyGraphService>().BuildGraphAsync(sln, "NoSuchType", null, "json", null);

            using var doc = JsonDocument.Parse(json);
            var nodes = doc.RootElement.GetProperty("nodes").EnumerateArray().ToList();
            var edges = doc.RootElement.GetProperty("edges").EnumerateArray().ToList();

            nodes.ShouldBeEmpty();
            edges.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task returns_empty_digraph_for_unknown_type_in_dot()
    {
        using var proj = BuildGraphProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var dot = await _services.Resolve<DependencyGraphService>().BuildGraphAsync(sln, "NoSuchType", null, "dot", null);

            Assert.Contains("digraph", dot, StringComparison.OrdinalIgnoreCase);
            dot.ShouldNotContain("->", Case.Sensitive);
        }
    }

    [Fact]
    public async Task scope_filter_restricts_nodes_to_matching_namespace()
    {
        using var proj = new AdhocProjectBuilder("ScopeApp");
        proj.AddSource("Types.cs", """
                                   namespace Foo { class Alpha { Beta _b; } class Beta { } }
                                   namespace Bar { class Gamma { } }
                                   """);
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var json = await _services.Resolve<DependencyGraphService>().BuildGraphAsync(sln, null, null, "json", "Foo");

            using var doc = JsonDocument.Parse(json);
            var nodeNames = doc.RootElement.GetProperty("nodes")
                .EnumerateArray().Select(n => n.GetString()).ToList();

            nodeNames.ShouldContain("Alpha");
            nodeNames.ShouldContain("Beta");
            nodeNames.ShouldNotContain("Gamma");
        }
    }
}
