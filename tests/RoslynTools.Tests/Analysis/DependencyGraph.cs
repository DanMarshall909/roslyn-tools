using System.Text.Json;
using RoslynTools.Commands;

namespace RoslynTools.Tests.Analysis;

public class DependencyGraph
{
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
            var json = await DependencyGraphCommand.BuildGraphAsync(sln, null, null, "json", null);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var nodes = root.GetProperty("nodes");
            var nodeNames = nodes.EnumerateArray().Select(n => n.GetString()).ToList();

            Assert.Contains("A", nodeNames);
            Assert.Contains("B", nodeNames);
            Assert.Contains("C", nodeNames);
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
            var json = await DependencyGraphCommand.BuildGraphAsync(sln, null, null, "json", null);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var edges = root.GetProperty("edges").EnumerateArray()
                .Select(e => (From: e.GetProperty("from").GetString()!, To: e.GetProperty("to").GetString()!))
                .ToList();

            Assert.Contains(edges, e => e.From == "A" && e.To == "B");
            Assert.Contains(edges, e => e.From == "C" && e.To == "A");
            Assert.Contains(edges, e => e.From == "C" && e.To == "B");
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
            var dot = await DependencyGraphCommand.BuildGraphAsync(sln, null, null, "dot", null);

            Assert.StartsWith("digraph", dot, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("->", dot);
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
            var json = await DependencyGraphCommand.BuildGraphAsync(sln, "A", 1, "json", null);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var nodes = root.GetProperty("nodes").EnumerateArray()
                .Select(n => n.GetString())
                .ToList();

            // A and its direct dependency B should be present
            Assert.Contains("A", nodes);
            Assert.Contains("B", nodes);

            // C depends on A, but C itself is not reachable from A within depth 1
            Assert.DoesNotContain("C", nodes);
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
            var json = await DependencyGraphCommand.BuildGraphAsync(
                sln, null, null, "json", null);

            using var doc = JsonDocument.Parse(json);
            var edges = doc.RootElement.GetProperty("edges").EnumerateArray()
                .Select(e => (From: e.GetProperty("from").GetString()!,
                    To: e.GetProperty("to").GetString()!))
                .ToList();

            Assert.Contains(edges, e => e.From == "A" && e.To == "B");
        }
    }
}
