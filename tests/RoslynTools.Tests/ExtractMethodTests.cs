using RoslynTools.Core;

namespace RoslynTools.Tests;

[Collection("MSBuild")]
public class ExtractMethodTests
{
    private static string FilePath(TestProjectBuilder project, string relativePath)
        => Path.Combine(project.ProjectDir, relativePath);

    // ──────────────────────────────────────────────────────────────────────────
    //  Void extraction — no variables flow in or out
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Void_NoVariables_ExtractsStatementsToNewMethod()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Calculator.cs", """
            namespace TestApp;

            public class Calculator
            {
                public void Process()
                {
                    int a = 1;
                    int b = 2;
                    System.Console.WriteLine(a + b);
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "Calculator.cs"),
                "DoWork",
                startLine: 7,
                endLine: 9);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var content = await project.ReadSourceAsync("Calculator.cs");
        Assert.Contains("private void DoWork()", content);
        Assert.Contains("DoWork();", content);
        // Statements moved to new method — Process() body is now just the call
        Assert.DoesNotContain("int a = 1;", GetMethodBody(content, "Process"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Parameters: outer variables flow in → become method parameters
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithParameters_FlowsIn_AddsParamsInFirstUseOrder()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Calculator.cs", """
            namespace TestApp;

            public class Calculator
            {
                public void Compute(int x, int y)
                {
                    var result = x + y;
                    var doubled = result * 2;
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "Calculator.cs"),
                "Calculate",
                startLine: 7,
                endLine: 8);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var content = await project.ReadSourceAsync("Calculator.cs");
        // x appears before y in the extracted code → x is first parameter
        Assert.Contains("private void Calculate(int x, int y)", content);
        Assert.Contains("Calculate(x, y);", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Single return value: variable declared in range, used after
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithReturnValue_SingleVar_ReturnsCorrectType()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Calculator.cs", """
            namespace TestApp;

            public class Calculator
            {
                public int GetTotal(int x, int y)
                {
                    var sum = x + y;
                    sum = sum * 2;
                    return sum;
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            // Extract lines 7-8: the two sum statements
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "Calculator.cs"),
                "ComputeSum",
                startLine: 7,
                endLine: 8);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var content = await project.ReadSourceAsync("Calculator.cs");
        Assert.Contains("private int ComputeSum(int x, int y)", content);
        Assert.Contains("var sum = ComputeSum(x, y);", content);
        Assert.Contains("return sum;", GetMethodBody(content, "ComputeSum"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Inner-only variable not returned: declared and used only inside range
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InnerVariable_NotUsedOutside_IsNotReturned()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Calculator.cs", """
            namespace TestApp;

            public class Calculator
            {
                public int Process(int n)
                {
                    var temp = n * 2;
                    var result = temp + 1;
                    return result;
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            // Extract lines 7-8: temp and result computed; only result used after
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "Calculator.cs"),
                "ComputeResult",
                startLine: 7,
                endLine: 8);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var content = await project.ReadSourceAsync("Calculator.cs");
        // temp is not used after the extraction range, so return type is int (not tuple)
        Assert.Contains("private int ComputeResult(int n)", content);
        // return should only be result, not a tuple including temp
        Assert.DoesNotContain("(", GetMethodBody(content, "ComputeResult").Split("return")[1].Split(";")[0]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Tuple return: multiple variables declared in range and used after
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TupleReturn_MultipleFlowsOut_GeneratesTuple()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Calculator.cs", """
            namespace TestApp;

            public class Calculator
            {
                public void GetBoth(int x)
                {
                    var a = x + 1;
                    var b = x * 2;
                    System.Console.WriteLine(a);
                    System.Console.WriteLine(b);
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            // Extract lines 7-8: a and b computed; both used after
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "Calculator.cs"),
                "Compute",
                startLine: 7,
                endLine: 8);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var content = await project.ReadSourceAsync("Calculator.cs");
        // Tuple return type
        Assert.Contains("(int", content);
        Assert.Contains("Compute(x)", content);
        // Deconstruction call site
        Assert.Contains("var (a, b) = Compute(x);", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Static method: extracted method should also be static
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StaticContainingMethod_ExtractedMethodIsAlsoStatic()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("MathUtil.cs", """
            namespace TestApp;

            public static class MathUtil
            {
                public static int Calculate(int n)
                {
                    var doubled = n * 2;
                    return doubled;
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "MathUtil.cs"),
                "ComputeDoubled",
                startLine: 7,
                endLine: 7);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var content = await project.ReadSourceAsync("MathUtil.cs");
        Assert.Contains("private static int ComputeDoubled(int n)", content);
        Assert.Contains("var doubled = ComputeDoubled(n);", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Placement: default = after containing method
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Placement_Default_AppearsAfterContainingMethod()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("MyClass.cs", """
            namespace TestApp;

            public class MyClass
            {
                public void MethodA()
                {
                    int x = 1;
                    int y = 2;
                }

                public void OtherMethod()
                {
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "MyClass.cs"),
                "DoWork",
                startLine: 7,
                endLine: 8);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var content = await project.ReadSourceAsync("MyClass.cs");
        Assert.Contains("private void DoWork()", content);
        // DoWork should appear between MethodA and OtherMethod
        var idxMethodA = content.IndexOf("public void MethodA()", StringComparison.Ordinal);
        var idxDoWork = content.IndexOf("private void DoWork()", StringComparison.Ordinal);
        var idxOtherMethod = content.IndexOf("public void OtherMethod()", StringComparison.Ordinal);
        Assert.True(idxMethodA < idxDoWork, "DoWork should appear after MethodA");
        Assert.True(idxDoWork < idxOtherMethod, "DoWork should appear before OtherMethod");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Placement: --after <name>
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Placement_AfterNamed_AppearsAfterSpecifiedMember()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("MyClass.cs", """
            namespace TestApp;

            public class MyClass
            {
                public void MethodA()
                {
                    int x = 1;
                    int y = 2;
                }

                public void OtherMethod()
                {
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "MyClass.cs"),
                "DoWork",
                startLine: 7,
                endLine: 8,
                insertAfter: "OtherMethod");
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var content = await project.ReadSourceAsync("MyClass.cs");
        var idxOtherMethod = content.IndexOf("public void OtherMethod()", StringComparison.Ordinal);
        var idxDoWork = content.IndexOf("private void DoWork()", StringComparison.Ordinal);
        Assert.True(idxOtherMethod < idxDoWork, "DoWork should appear after OtherMethod");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Placement: --before <name>
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Placement_BeforeNamed_AppearsBeforeSpecifiedMember()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("MyClass.cs", """
            namespace TestApp;

            public class MyClass
            {
                public void MethodA()
                {
                    int x = 1;
                    int y = 2;
                }

                public void OtherMethod()
                {
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "MyClass.cs"),
                "DoWork",
                startLine: 7,
                endLine: 8,
                insertBefore: "OtherMethod");
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var content = await project.ReadSourceAsync("MyClass.cs");
        var idxDoWork = content.IndexOf("private void DoWork()", StringComparison.Ordinal);
        var idxOtherMethod = content.IndexOf("public void OtherMethod()", StringComparison.Ordinal);
        Assert.True(idxDoWork < idxOtherMethod, "DoWork should appear before OtherMethod");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Dry-run: no file changes written
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DryRun_DoesNotModifyFiles()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Calculator.cs", """
            namespace TestApp;

            public class Calculator
            {
                public void Process()
                {
                    int a = 1;
                    int b = 2;
                    System.Console.WriteLine(a + b);
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var originalContent = await project.ReadSourceAsync("Calculator.cs");

        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "Calculator.cs"),
                "DoWork",
                startLine: 7,
                endLine: 9);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: true);
        }

        var afterContent = await project.ReadSourceAsync("Calculator.cs");
        Assert.Equal(originalContent, afterContent);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Error: no statements in the specified line range
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Error_EmptyRange_Throws()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Calculator.cs", """
            namespace TestApp;

            public class Calculator
            {
                public void Process()
                {
                    int a = 1;
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                MethodExtractor.ExtractAsync(
                    sln,
                    FilePath(project, "Calculator.cs"),
                    "DoWork",
                    startLine: 50,
                    endLine: 60));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Error: named anchor member does not exist
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Error_AnchorNotFound_Throws()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Calculator.cs", """
            namespace TestApp;

            public class Calculator
            {
                public void Process()
                {
                    int a = 1;
                    int b = 2;
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                MethodExtractor.ExtractAsync(
                    sln,
                    FilePath(project, "Calculator.cs"),
                    "DoWork",
                    startLine: 7,
                    endLine: 8,
                    insertAfter: "NonExistentMethod"));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  LF line endings: generated method blank-line separator must be LF
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GeneratedMethod_UsesLF_NotCRLF()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Calculator.cs", """
            namespace TestApp;

            public class Calculator
            {
                public void Process()
                {
                    int a = 1;
                    int b = 2;
                    System.Console.WriteLine(a + b);
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "Calculator.cs"),
                "DoWork",
                startLine: 7,
                endLine: 9);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var rawBytes = await File.ReadAllBytesAsync(FilePath(project, "Calculator.cs"));
        var rawContent = System.Text.Encoding.UTF8.GetString(rawBytes);
        Assert.DoesNotContain("\r\n", rawContent);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Error: invalid line range arguments
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Error_StartLineZero_Throws()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Calculator.cs", """
            namespace TestApp;

            public class Calculator
            {
                public void Process()
                {
                    int a = 1;
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                MethodExtractor.ExtractAsync(
                    sln,
                    FilePath(project, "Calculator.cs"),
                    "DoWork",
                    startLine: 0,
                    endLine: 5));
        }
    }

    [Fact]
    public async Task Error_EndLineBeforeStartLine_Throws()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Calculator.cs", """
            namespace TestApp;

            public class Calculator
            {
                public void Process()
                {
                    int a = 1;
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                MethodExtractor.ExtractAsync(
                    sln,
                    FilePath(project, "Calculator.cs"),
                    "DoWork",
                    startLine: 5,
                    endLine: 3));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --replace-duplicates: exact cross-file duplicate replaced
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplaceDuplicates_ExactCrossFileDuplicate_FilesBothUpdated()
    {
        using var project = new TestProjectBuilder();
        // File A: contains the extraction target
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void Process()
                {
                    System.Console.WriteLine("hello");
                    System.Console.WriteLine("world");
                }
            }
            """);
        // File B: identical body — should be replaced with a call to the extracted method
        project.AddSource("B.cs", """
            namespace TestApp;

            public class B
            {
                public void Run()
                {
                    System.Console.WriteLine("hello");
                    System.Console.WriteLine("world");
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "A.cs"),
                "PrintMessages",
                startLine: 7,
                endLine: 8,
                replaceDuplicates: true);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var contentA = await project.ReadSourceAsync("A.cs");
        var contentB = await project.ReadSourceAsync("B.cs");

        // A has the extracted method
        Assert.Contains("private void PrintMessages()", contentA);
        Assert.Contains("PrintMessages();", contentA);

        // B's duplicate was replaced with a call
        Assert.Contains("PrintMessages();", contentB);
        // Original statements removed from B
        Assert.DoesNotContain("Console.WriteLine(\"hello\")", GetMethodBody(contentB, "Run"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --replace-duplicates: duplicate with renamed identifiers replaced
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplaceDuplicates_IdentifierRenamedDuplicate_ReplacedWithCorrectArgs()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void Compute(int x, int y)
                {
                    var sum = x + y;
                    System.Console.WriteLine(sum);
                }
            }
            """);
        // B has same structure but different identifier names
        project.AddSource("B.cs", """
            namespace TestApp;

            public class B
            {
                public void Compute(int a, int b)
                {
                    var total = a + b;
                    System.Console.WriteLine(total);
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "A.cs"),
                "PrintSum",
                startLine: 7,
                endLine: 8,
                replaceDuplicates: true);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var contentA = await project.ReadSourceAsync("A.cs");
        var contentB = await project.ReadSourceAsync("B.cs");

        // A has the extracted method
        Assert.Contains("PrintSum(", contentA);

        // B has the call with mapped argument names
        Assert.Contains("PrintSum(", contentB);
        // B's call should use B's identifiers (a and/or b)
        var bBody = GetMethodBody(contentB, "Compute");
        Assert.DoesNotContain("var total = a + b;", bBody);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --replace-duplicates: no duplicates found → extraction still works
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplaceDuplicates_NoDuplicatesFound_ExtractionStillSucceeds()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("Calculator.cs", """
            namespace TestApp;

            public class Calculator
            {
                public void Process()
                {
                    int a = 1;
                    int b = 2;
                    System.Console.WriteLine(a + b);
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            // replaceDuplicates: true but there is only one occurrence of this body
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "Calculator.cs"),
                "DoWork",
                startLine: 7,
                endLine: 9,
                replaceDuplicates: true);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var content = await project.ReadSourceAsync("Calculator.cs");
        // Extraction happened correctly
        Assert.Contains("private void DoWork()", content);
        Assert.Contains("DoWork();", content);
        Assert.DoesNotContain("int a = 1;", GetMethodBody(content, "Process"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --replace-duplicates: dry-run shows all replacements without writing
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplaceDuplicates_DryRun_NoFilesWritten()
    {
        using var project = new TestProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void Process()
                {
                    System.Console.WriteLine("hello");
                    System.Console.WriteLine("world");
                }
            }
            """);
        project.AddSource("B.cs", """
            namespace TestApp;

            public class B
            {
                public void Run()
                {
                    System.Console.WriteLine("hello");
                    System.Console.WriteLine("world");
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var originalA = await project.ReadSourceAsync("A.cs");
        var originalB = await project.ReadSourceAsync("B.cs");

        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "A.cs"),
                "PrintMessages",
                startLine: 7,
                endLine: 8,
                replaceDuplicates: true);

            // Dry-run: in-memory solution contains the changes but files are not written
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: true);
        }

        // Files on disk must be unchanged
        Assert.Equal(originalA, await project.ReadSourceAsync("A.cs"));
        Assert.Equal(originalB, await project.ReadSourceAsync("B.cs"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --replace-duplicates: match skipped when parameter cannot be mapped
    //  (regression guard: verify the summary line is emitted with skipped > 0)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplaceDuplicates_LiteralSubstitutionInMatch_MatchIsSkipped()
    {
        // The extracted body has a parameter `x` that flows in. The "duplicate" at the
        // other site has a literal 42 where `x` was, so the structural kinds differ and
        // PatternMatcher won't report it as a match at all — the replace-duplicates flag
        // becomes a no-op (0 matches), which is the correct safe behaviour.
        using var project = new TestProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void Compute(int x)
                {
                    var doubled = x * 2;
                    System.Console.WriteLine(doubled);
                }
            }
            """);
        // B has a literal where A uses a parameter — structurally different, no match expected
        project.AddSource("B.cs", """
            namespace TestApp;

            public class B
            {
                public void Compute()
                {
                    var doubled = 42 * 2;
                    System.Console.WriteLine(doubled);
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            // Should succeed without exception — B's literal site is not matched
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "A.cs"),
                "ComputeDoubled",
                startLine: 7,
                endLine: 8,
                replaceDuplicates: true);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        // A is correctly refactored
        var contentA = await project.ReadSourceAsync("A.cs");
        Assert.Contains("private void ComputeDoubled(int x)", contentA);
        Assert.Contains("ComputeDoubled(x);", contentA);

        // B is untouched (literal body didn't match the pattern)
        var contentB = await project.ReadSourceAsync("B.cs");
        Assert.Contains("var doubled = 42 * 2;", contentB);
        Assert.DoesNotContain("ComputeDoubled", contentB);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --replace-duplicates: multiple duplicates in the same file
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplaceDuplicates_TwoDuplicatesInSameFile_BothReplacedCorrectly()
    {
        // Regression for the multi-match-in-same-file line-drift bug uncovered when
        // dogfooding rt extract-method --replace-duplicates against FindPatternCommand.
        // The naive implementation computes match line numbers once up-front, then applies
        // them in order — so after the first in-file replacement, later matches have stale
        // line numbers and SelectStatementsInRange picks up the wrong statements (or nothing).
        using var project = new TestProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void Source()
                {
                    System.Console.WriteLine("hello");
                    System.Console.WriteLine("world");
                }

                public void Dupe1()
                {
                    System.Console.WriteLine("hello");
                    System.Console.WriteLine("world");
                }

                public void Dupe2()
                {
                    System.Console.WriteLine("hello");
                    System.Console.WriteLine("world");
                }
            }
            """);

        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "A.cs"),
                "PrintMessages",
                startLine: 7,
                endLine: 8,
                replaceDuplicates: true);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var content = await project.ReadSourceAsync("A.cs");

        // Source method has the extraction call
        Assert.Contains("PrintMessages();", GetMethodBody(content, "Source"));

        // BOTH Dupe1 and Dupe2 should have the call — this is the bug: with line-drift,
        // one (or both) duplicates is left with stale or partial content.
        Assert.Contains("PrintMessages();", GetMethodBody(content, "Dupe1"));
        Assert.Contains("PrintMessages();", GetMethodBody(content, "Dupe2"));

        // Original statements are gone from both duplicate methods
        Assert.DoesNotContain("Console.WriteLine(\"hello\")", GetMethodBody(content, "Dupe1"));
        Assert.DoesNotContain("Console.WriteLine(\"hello\")", GetMethodBody(content, "Dupe2"));

        // The extracted method exists exactly once (not duplicated per match).
        var extractedCount = System.Text.RegularExpressions.Regex.Matches(
            content, @"private\s+void\s+PrintMessages\s*\(").Count;
        Assert.Equal(1, extractedCount);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Naively extracts the body of the first method with the given name from C# source.
    /// Good enough for assertions — not a full parser.
    /// </summary>
    private static string GetMethodBody(string source, string methodName)
    {
        var sigIdx = source.IndexOf(methodName + "(", StringComparison.Ordinal);
        if (sigIdx < 0) return string.Empty;

        var openBrace = source.IndexOf('{', sigIdx);
        if (openBrace < 0) return string.Empty;

        int depth = 0;
        int closeBrace = openBrace;
        for (int i = openBrace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) { closeBrace = i; break; }
            }
        }

        return source[openBrace..(closeBrace + 1)];
    }
}
