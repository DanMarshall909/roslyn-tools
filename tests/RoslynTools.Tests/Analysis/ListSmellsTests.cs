using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Tests.Analysis;

public class ListSmellsTests
{
    // ---------------------------------------------------------------------------
    // Test 1: Large class is reported
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ListSmells_LargeClass_Reported()
    {
        using var project = new AdhocProjectBuilder();

        var largeClassLines = new System.Text.StringBuilder();
        largeClassLines.AppendLine("public class BigClass {");
        for (int i = 0; i < 305; i++) largeClassLines.AppendLine($"    public int Prop{i} {{ get; set; }}");
        largeClassLines.AppendLine("}");
        project.AddSource("BigClass.cs", largeClassLines.ToString());

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var smells = await SmellAnalyzer.AnalyzeAsync(sln, maxClassLines: 300, maxMethodLines: 40, maxParams: 7, scope: null);

            var largeClass = smells.Where(s => s.SmellType == "LargeClass").ToList();
            Assert.NotEmpty(largeClass);
            Assert.Contains(largeClass, s => s.TypeName == "BigClass");
        }
    }

    // ---------------------------------------------------------------------------
    // Test 2: Long method is reported
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ListSmells_LongMethod_Reported()
    {
        using var project = new AdhocProjectBuilder();

        var longMethodLines = new System.Text.StringBuilder();
        longMethodLines.AppendLine("public class MyService {");
        longMethodLines.AppendLine("    public void DoWork() {");
        for (int i = 0; i < 45; i++) longMethodLines.AppendLine($"        var x{i} = {i};");
        longMethodLines.AppendLine("    }");
        longMethodLines.AppendLine("}");
        project.AddSource("MyService.cs", longMethodLines.ToString());

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var smells = await SmellAnalyzer.AnalyzeAsync(sln, maxClassLines: 300, maxMethodLines: 40, maxParams: 7, scope: null);

            var longMethods = smells.Where(s => s.SmellType == "LongMethod").ToList();
            Assert.NotEmpty(longMethods);
            Assert.Contains(longMethods, s => s.MemberName == "DoWork");
        }
    }

    // ---------------------------------------------------------------------------
    // Test 3: Too many constructor dependencies reported
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ListSmells_TooManyDependencies_Reported()
    {
        using var project = new AdhocProjectBuilder();

        project.AddSource("FatService.cs", """
            public interface IA {} public interface IB {} public interface IC {}
            public interface ID {} public interface IE {} public interface IF {}
            public interface IG {} public interface IH {}
            public class FatService {
                public FatService(IA a, IB b, IC c, ID d, IE e, IF f, IG g, IH h) {}
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var smells = await SmellAnalyzer.AnalyzeAsync(sln, maxClassLines: 300, maxMethodLines: 40, maxParams: 7, scope: null);

            var tooDeps = smells.Where(s => s.SmellType == "TooManyDependencies").ToList();
            Assert.NotEmpty(tooDeps);
            Assert.Contains(tooDeps, s => s.TypeName == "FatService");
        }
    }

    // ---------------------------------------------------------------------------
    // Test 4: Clean project returns no smells
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ListSmells_CleanProject_ReturnsEmpty()
    {
        using var project = new AdhocProjectBuilder();

        project.AddSource("SmallClass.cs", """
            public class SmallClass
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";

                public void DoSomething()
                {
                    var x = 1 + 2;
                    Console.WriteLine(x);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var smells = await SmellAnalyzer.AnalyzeAsync(sln, maxClassLines: 300, maxMethodLines: 40, maxParams: 7, scope: null);

            Assert.Empty(smells);
        }
    }

    // ---------------------------------------------------------------------------
    // Test 5: Custom threshold respected
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ListSmells_CustomThreshold_Respected()
    {
        using var project = new AdhocProjectBuilder();

        // A class of ~55 lines — under default 300 but over custom threshold of 50
        var mediumClassLines = new System.Text.StringBuilder();
        mediumClassLines.AppendLine("public class MediumClass {");
        for (int i = 0; i < 53; i++) mediumClassLines.AppendLine($"    public int Prop{i} {{ get; set; }}");
        mediumClassLines.AppendLine("}");
        project.AddSource("MediumClass.cs", mediumClassLines.ToString());

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // Default threshold — should NOT flag
            var smellsDefault = await SmellAnalyzer.AnalyzeAsync(sln, maxClassLines: 300, maxMethodLines: 40, maxParams: 7, scope: null);
            Assert.DoesNotContain(smellsDefault, s => s.SmellType == "LargeClass" && s.TypeName == "MediumClass");

            // Custom threshold of 50 — SHOULD flag
            var smellsCustom = await SmellAnalyzer.AnalyzeAsync(sln, maxClassLines: 50, maxMethodLines: 40, maxParams: 7, scope: null);
            Assert.Contains(smellsCustom, s => s.SmellType == "LargeClass" && s.TypeName == "MediumClass");
        }
    }
}
