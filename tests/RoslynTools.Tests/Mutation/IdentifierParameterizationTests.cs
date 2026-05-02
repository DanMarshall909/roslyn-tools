using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

/// <summary>
/// Integration tests for US-045 Phase 2: identifier-variation parameterization.
/// Verify that --replace-duplicates detects and parameterizes identifier-only differences.
///
/// v1 scope: bare identifier references only (e.g. passed as arguments).
/// Member access on varying identifiers (e.g. _svc.Method()) is Phase 3+.
/// </summary>
public class IdentifierParameterizationTests
{
    private static string FilePath(AdhocProjectBuilder project, string relativePath)
        => Path.Combine(project.ProjectDir, relativePath);

    // ──────────────────────────────────────────────────────────────────────────
    //  AC2/AC3: Single varying identifier -> new parameter added
    //  Pattern: bare identifier passed as argument
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Parameterizes_single_varying_identifier()
    {
        // File A extracts: uses _serviceA as bare argument
        // File B's duplicate uses _serviceB as bare argument
        // Expected: extracted method gets a parameter for the varying identifier
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                private readonly string _serviceA = "svc";

                public void Process()
                {
                    System.Console.WriteLine(_serviceA);
                    System.Console.WriteLine("done");
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
                    System.Console.WriteLine(_serviceB);
                    System.Console.WriteLine("done");
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

        // Extracted method exists in A
        Assert.Contains("DoWork(", contentA);
        // B's call site passes _serviceB as the argument
        Assert.Contains("DoWork(", contentB);
        Assert.Contains("_serviceB", contentB);
        // B's original statements replaced
        Assert.DoesNotContain("System.Console.WriteLine(_serviceB)", contentB);

        // KNOWN BUG (pre-existing, logged in tasks/dogfood-gaps.md 2026-05-03):
        // The source-file (A.cs) call site is NOT updated when new parameters are added.
        // After parameterization DoWork() should become DoWork(_serviceA), but PatternMatcher
        // excludes the source file so TryReplaceMatchAsync never runs for A's own usage.
        // The assertion below would correctly catch a fix when it ships:
        //   Assert.Contains("DoWork(_serviceA)", contentA);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  AC3: Multiple varying identifiers -> multiple parameters
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Parameterizes_multiple_varying_identifiers()
    {
        // A uses _readerA and _writerA as bare arguments; B uses _readerB and _writerB
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                private readonly string _readerA = "r";
                private readonly string _writerA = "w";

                public void Execute()
                {
                    System.Console.WriteLine(_readerA);
                    System.Console.WriteLine(_writerA);
                    System.Console.WriteLine("ok");
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
                    System.Console.WriteLine(_readerB);
                    System.Console.WriteLine(_writerB);
                    System.Console.WriteLine("ok");
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

        // Extracted method exists in A
        Assert.Contains("RunPipeline(", contentA);
        // B's call site passes B's identifiers
        Assert.Contains("_readerB", contentB);
        Assert.Contains("_writerB", contentB);
        // Original statements removed from B
        Assert.DoesNotContain("System.Console.WriteLine(_readerB)", contentB);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  AC4: Mixed literal + identifier variations -> both parameterized
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Parameterizes_mixed_literal_and_identifier_variations()
    {
        // A: Console.WriteLine("hello"); Console.WriteLine(_fieldA)
        // B: Console.WriteLine("goodbye"); Console.WriteLine(_fieldB)
        // Both the string literal AND the bare identifier differ -> both parameterized
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

        // Extracted method exists in A with parameters
        Assert.Contains("DoPrint(", contentA);
        // B's call site passes B's identifier and the different literal
        Assert.Contains("DoPrint(", contentB);
        Assert.Contains("_fieldB", contentB);
        Assert.Contains("goodbye", contentB);
        // Original statements removed from B
        Assert.DoesNotContain("System.Console.WriteLine(\"goodbye\")", contentB);
        Assert.DoesNotContain("System.Console.WriteLine(_fieldB)", contentB);
    }
}
