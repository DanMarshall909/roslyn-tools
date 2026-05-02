using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

/// <summary>
/// Integration tests for US-045 Phase 2: identifier-variation parameterization.
/// Verify that --replace-duplicates detects and parameterizes identifier-only differences.
/// </summary>
public class IdentifierParameterizationTests
{
    private static string FilePath(AdhocProjectBuilder project, string relativePath)
        => Path.Combine(project.ProjectDir, relativePath);

    // ──────────────────────────────────────────────────────────────────────────
    //  AC2/AC3: Single varying identifier → new parameter added
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Parameterizes_single_varying_identifier()
    {
        // File A extracts: uses _serviceA
        // File B's duplicate uses _serviceB
        // Expected: extracted method gets parameter for _serviceA/_serviceB
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                private readonly string _serviceA = "svc";

                public void Process()
                {
                    var result = _serviceA.Length;
                    System.Console.WriteLine(result);
                }
            }
            """);
        project.AddSource("B.cs", """
            namespace TestApp;

            public class B
            {
                private readonly string _serviceB = "svc2";

                public void Process()
                {
                    var result = _serviceB.Length;
                    System.Console.WriteLine(result);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "A.cs"),
                "DoWork",
                startLine: 9,
                endLine: 10,
                replaceDuplicates: true);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var contentA = await project.ReadSourceAsync("A.cs");
        var contentB = await project.ReadSourceAsync("B.cs");

        // Method has a parameter for the varying identifier
        Assert.Contains("DoWork(", contentA);
        // B should have a call with _serviceB passed as the argument
        Assert.Contains("DoWork(", contentB);
        Assert.Contains("_serviceB", contentB);
        // B's original statements replaced
        Assert.DoesNotContain("var result = _serviceB.Length;", contentB);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  AC3: Multiple varying identifiers → multiple parameters
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Parameterizes_multiple_varying_identifiers()
    {
        // A uses _readerA and _writerA; B uses _readerB and _writerB
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                private readonly string _readerA = "r";
                private readonly string _writerA = "w";

                public void Execute()
                {
                    var r = _readerA.Length;
                    var w = _writerA.Length;
                    System.Console.WriteLine(r + w);
                }
            }
            """);
        project.AddSource("B.cs", """
            namespace TestApp;

            public class B
            {
                private readonly string _readerB = "r2";
                private readonly string _writerB = "w2";

                public void Execute()
                {
                    var r = _readerB.Length;
                    var w = _writerB.Length;
                    System.Console.WriteLine(r + w);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "A.cs"),
                "RunPipeline",
                startLine: 10,
                endLine: 12,
                replaceDuplicates: true);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var contentA = await project.ReadSourceAsync("A.cs");
        var contentB = await project.ReadSourceAsync("B.cs");

        // Method signature should have parameters for both varying identifiers
        Assert.Contains("RunPipeline(", contentA);
        // B's call should pass B's identifiers
        Assert.Contains("_readerB", contentB);
        Assert.Contains("_writerB", contentB);
        // Original statements removed from B
        Assert.DoesNotContain("var r = _readerB.Length;", contentB);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  AC4: Mixed literal + identifier variations → both parameterized
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Parameterizes_mixed_literal_and_identifier_variations()
    {
        // A: Console.WriteLine("hello"); Console.WriteLine(_fieldA.Length.ToString());
        // B: Console.WriteLine("goodbye"); Console.WriteLine(_fieldB.Length.ToString());
        // Both string literal AND identifier differ → both parameterized
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                private readonly string _fieldA = "a";

                public void Print()
                {
                    System.Console.WriteLine("hello");
                    System.Console.WriteLine(_fieldA);
                }
            }
            """);
        project.AddSource("B.cs", """
            namespace TestApp;

            public class B
            {
                private readonly string _fieldB = "b";

                public void Print()
                {
                    System.Console.WriteLine("goodbye");
                    System.Console.WriteLine(_fieldB);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "A.cs"),
                "DoPrint",
                startLine: 9,
                endLine: 10,
                replaceDuplicates: true);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var contentA = await project.ReadSourceAsync("A.cs");
        var contentB = await project.ReadSourceAsync("B.cs");

        // Method should have parameters
        Assert.Contains("DoPrint(", contentA);
        // B's call uses B's identifier and the different literal
        Assert.Contains("DoPrint(", contentB);
        Assert.Contains("_fieldB", contentB);
        Assert.Contains("goodbye", contentB);
        // Original statements removed
        Assert.DoesNotContain("System.Console.WriteLine(\"goodbye\")", contentB);
        Assert.DoesNotContain("System.Console.WriteLine(_fieldB)", contentB);
    }
}
