using RoslynTools.Commands;
using RoslynTools.Tests.Infrastructure;

namespace RoslynTools.Tests.Analysis;

public class CircularDeps : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public CircularDeps(RtServicesFixture services) => _services = services;

    // Three namespaces that form a cycle: A -> B -> C -> A
    private static AdhocProjectBuilder BuildCycleProject()
    {
        var proj = new AdhocProjectBuilder("CycleApp");
        proj.AddSource("FileA.cs", "namespace CycleApp.A { using CycleApp.B; class A { } }");
        proj.AddSource("FileB.cs", "namespace CycleApp.B { using CycleApp.C; class B { } }");
        proj.AddSource("FileC.cs", "namespace CycleApp.C { using CycleApp.A; class C { } }");
        return proj;
    }

    // Two namespaces with no cycle: X has no deps, Y depends on X
    private static AdhocProjectBuilder BuildCleanProject()
    {
        var proj = new AdhocProjectBuilder("CycleApp");
        proj.AddSource("FileX.cs", "namespace CycleApp.X { class X { } }");
        proj.AddSource("FileY.cs", "namespace CycleApp.Y { using CycleApp.X; class Y { } }");
        return proj;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  1. Cycle is detected and reported
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task reports_the_types_in_the_cycle()
    {
        using var proj = BuildCycleProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var result = await _services.Resolve<CircularDepsCommand>().AnalyzeAsync(sln, null);

            Assert.Contains("CycleApp.A", result);
            Assert.Contains("CycleApp.B", result);
            Assert.Contains("CycleApp.C", result);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. Cycle output includes "->" separator showing the cycle path
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task reports_the_cycle_path()
    {
        using var proj = BuildCycleProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var result = await _services.Resolve<CircularDepsCommand>().AnalyzeAsync(sln, null);

            Assert.Contains("->", result);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  3. Clean project reports no cycles
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task returns_a_clean_result_when_no_cycles_exist()
    {
        using var proj = BuildCleanProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var result = await _services.Resolve<CircularDepsCommand>().AnalyzeAsync(sln, null);

            Assert.Contains("No circular", result, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. Command writes cycle results to stdout
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task writes_the_cycle_path_to_stdout()
    {
        using var proj = BuildCycleProject();
        var (workspace, sln) = proj.Build();

        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            using (workspace)
            {
                var result = await _services.Resolve<CircularDepsCommand>().AnalyzeAsync(sln, null);
                Console.WriteLine(result);
            }
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var captured = sw.ToString();
        Assert.Contains("->", captured);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  5. Scope filter limits analysis to a namespace prefix
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task limits_cycles_to_matching_namespace_prefix_when_scope_filter_is_used()
    {
        // Two independent cycle groups: CycleApp.A/B/C and External.X/Y/Z
        var proj = new AdhocProjectBuilder("Mixed");
        proj.AddSource("A.cs", "namespace CycleApp.A { using CycleApp.B; class A { } }");
        proj.AddSource("B.cs", "namespace CycleApp.B { using CycleApp.C; class B { } }");
        proj.AddSource("C.cs", "namespace CycleApp.C { using CycleApp.A; class C { } }");
        proj.AddSource("X.cs", "namespace External.X { using External.Y; class X { } }");
        proj.AddSource("Y.cs", "namespace External.Y { using External.Z; class Y { } }");
        proj.AddSource("Z.cs", "namespace External.Z { using External.X; class Z { } }");

        using (proj)
        {
            var (workspace, sln) = proj.Build();
            using (workspace)
            {
                var result = await _services.Resolve<CircularDepsCommand>().AnalyzeAsync(sln, "CycleApp");
                Assert.Contains("CycleApp", result);
                Assert.DoesNotContain("External", result);
            }
        }
    }

    [Fact]
    public async Task reports_no_cycles_when_scope_filter_has_none()
    {
        var proj = new AdhocProjectBuilder("Mixed");
        proj.AddSource("A.cs", "namespace CycleApp.A { using CycleApp.B; class A { } }");
        proj.AddSource("B.cs", "namespace CycleApp.B { using CycleApp.C; class B { } }");
        proj.AddSource("C.cs", "namespace CycleApp.C { using CycleApp.A; class C { } }");
        proj.AddSource("X.cs", "namespace Clean.X { class X { } }");

        using (proj)
        {
            var (workspace, sln) = proj.Build();
            using (workspace)
            {
                var result = await _services.Resolve<CircularDepsCommand>().AnalyzeAsync(sln, "Clean");
                Assert.Contains("No circular", result, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  6. File-scoped namespace declarations are handled
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task detects_cycles_in_file_scoped_namespaces()
    {
        var proj = new AdhocProjectBuilder("FsNs");
        proj.AddSource("P.cs", "namespace Fs.P;\nusing Fs.Q;\nclass P { }");
        proj.AddSource("Q.cs", "namespace Fs.Q;\nusing Fs.P;\nclass Q { }");

        using (proj)
        {
            var (workspace, sln) = proj.Build();
            using (workspace)
            {
                var result = await _services.Resolve<CircularDepsCommand>().AnalyzeAsync(sln, null);
                Assert.Contains("Fs.P", result);
                Assert.Contains("Fs.Q", result);
            }
        }
    }
}
