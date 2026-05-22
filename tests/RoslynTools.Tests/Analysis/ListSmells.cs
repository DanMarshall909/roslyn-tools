using System.Text;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public class ListSmells
{
    // ---------------------------------------------------------------------------
    // Test 1: Large class is reported
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task reports_large_classes()
    {
        using var project = new AdhocProjectBuilder();

        var largeClassLines = new StringBuilder();
        largeClassLines.AppendLine("public class BigClass {");
        for (var i = 0; i < 305; i++) largeClassLines.AppendLineF($"    public int Prop{i} {{ get; set; }}");
        largeClassLines.AppendLine("}");
        project.AddSource("BigClass.cs", largeClassLines.ToString());

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var smells = await SmellAnalyzer.AnalyzeAsync(sln, 300, 40, 7, null);

            var largeClass = smells.Where(s => s.SmellType == "LargeClass").ToList();
            largeClass.ShouldNotBeEmpty();
            largeClass.ShouldContain(s => s.TypeName == "BigClass");
        }
    }

    // ---------------------------------------------------------------------------
    // Test 2: Long method is reported
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task reports_long_methods()
    {
        using var project = new AdhocProjectBuilder();

        var longMethodLines = new StringBuilder();
        longMethodLines.AppendLine("public class MyService {");
        longMethodLines.AppendLine("    public void DoWork() {");
        for (var i = 0; i < 45; i++) longMethodLines.AppendLineF($"        var x{i} = {i};");
        longMethodLines.AppendLine("    }");
        longMethodLines.AppendLine("}");
        project.AddSource("MyService.cs", longMethodLines.ToString());

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var smells = await SmellAnalyzer.AnalyzeAsync(sln, 300, 40, 7, null);

            var longMethods = smells.Where(s => s.SmellType == "LongMethod").ToList();
            longMethods.ShouldNotBeEmpty();
            longMethods.ShouldContain(s => s.MemberName == "DoWork");
        }
    }

    // ---------------------------------------------------------------------------
    // Test 3: Too many constructor dependencies reported
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task reports_types_with_too_many_dependencies()
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
            var smells = await SmellAnalyzer.AnalyzeAsync(sln, 300, 40, 7, null);

            var tooDeps = smells.Where(s => s.SmellType == "TooManyDependencies").ToList();
            tooDeps.ShouldNotBeEmpty();
            tooDeps.ShouldContain(s => s.TypeName == "FatService");
        }
    }

    // ---------------------------------------------------------------------------
    // Test 4: Clean project returns no smells
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task returns_no_smells_for_a_clean_project()
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
            var smells = await SmellAnalyzer.AnalyzeAsync(sln, 300, 40, 7, null);

            smells.ShouldBeEmpty();
        }
    }

    // ---------------------------------------------------------------------------
    // Test 5: Custom threshold respected
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task honours_custom_thresholds()
    {
        using var project = new AdhocProjectBuilder();

        // A class of ~55 lines — under default 300 but over custom threshold of 50
        var mediumClassLines = new StringBuilder();
        mediumClassLines.AppendLine("public class MediumClass {");
        for (var i = 0; i < 53; i++) mediumClassLines.AppendLineF($"    public int Prop{i} {{ get; set; }}");
        mediumClassLines.AppendLine("}");
        project.AddSource("MediumClass.cs", mediumClassLines.ToString());

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // Default threshold — should NOT flag
            var smellsDefault = await SmellAnalyzer.AnalyzeAsync(sln, 300, 40, 7, null);
            smellsDefault.ShouldNotContain(s => s.SmellType == "LargeClass" && s.TypeName == "MediumClass");

            // Custom threshold of 50 — SHOULD flag
            var smellsCustom = await SmellAnalyzer.AnalyzeAsync(sln, 50, 40, 7, null);
            smellsCustom.ShouldContain(s => s.SmellType == "LargeClass" && s.TypeName == "MediumClass");
        }
    }

    [Fact]
    public async Task matches_namespace_prefix_scope()
    {
        using var project = new AdhocProjectBuilder();

        var largeClassLines = new StringBuilder();
        largeClassLines.AppendLine("namespace RoslynTools.Commands;");
        largeClassLines.AppendLine("public class FatCommand {");
        for (var i = 0; i < 55; i++) largeClassLines.AppendLineF($"    public int Prop{i} {{ get; set; }}");
        largeClassLines.AppendLine("}");
        project.AddSource("FatCommand.cs", largeClassLines.ToString());

        project.AddSource("Other.cs", """
                                      namespace OtherNamespace;

                                      public class OtherCommand
                                      {
                                          public int Id { get; set; }
                                      }
                                      """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var smells = await SmellAnalyzer.AnalyzeAsync(
                sln,
                50,
                40,
                7,
                "RoslynTools.Commands");

            smells.ShouldContain(s => s.TypeName == "FatCommand");
            smells.ShouldNotContain(s => s.TypeName == "OtherCommand");
        }
    }
}
