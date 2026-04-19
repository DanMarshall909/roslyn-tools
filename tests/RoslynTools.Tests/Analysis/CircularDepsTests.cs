using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Analysis;

public class CircularDepsTests
{
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
    public async Task CircularDeps_DetectsCycle()
    {
        using var proj = BuildCycleProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var result = await CircularDepsCommand.AnalyzeAsync(sln, scope: null);

            Assert.Contains("CycleApp.A", result);
            Assert.Contains("CycleApp.B", result);
            Assert.Contains("CycleApp.C", result);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. Cycle output includes "->" separator showing the cycle path
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CircularDeps_CyclePathInOutput()
    {
        using var proj = BuildCycleProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var result = await CircularDepsCommand.AnalyzeAsync(sln, scope: null);

            Assert.Contains("->", result);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  3. Clean project reports no cycles
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CircularDeps_NoCycle_ReturnsClean()
    {
        using var proj = BuildCleanProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var result = await CircularDepsCommand.AnalyzeAsync(sln, scope: null);

            Assert.Contains("No circular", result, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. Command writes cycle results to stdout
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CircularDeps_OutputToStdout()
    {
        using var proj = BuildCycleProject();
        var (workspace, sln) = proj.Build();

        var originalOut = Console.Out;
        using var sw = new System.IO.StringWriter();
        Console.SetOut(sw);
        try
        {
            using (workspace)
            {
                var result = await CircularDepsCommand.AnalyzeAsync(sln, scope: null);
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
}
