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
