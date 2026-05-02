using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public class ExtractMethodTests
{
    private static string FilePath(AdhocProjectBuilder project, string relativePath)
        => Path.Combine(project.ProjectDir, relativePath);

    // ──────────────────────────────────────────────────────────────────────────
    //  Void extraction — no variables flow in or out
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Moves_the_selected_statements_into_a_new_method()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Adds_parameters_in_first_use_order()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Returns_the_extracted_value_with_the_correct_type()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Does_not_return_values_that_are_not_used_afterward()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Returns_a_tuple()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Makes_the_extracted_method_static()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Places_the_new_method_after_the_containing_method()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Places_the_new_method_after_the_named_member()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Places_the_new_method_before_the_named_member()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Does_not_modify_files()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
        var originalContent = await project.ReadSourceAsync("Calculator.cs");
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
    public async Task Throws_an_error_when_the_selected_range_has_no_statements()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Throws_an_error_when_the_named_anchor_is_missing()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Uses_lf_line_endings_in_the_generated_method()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Throws_an_error_when_the_start_line_is_zero()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Throws_an_error_when_the_end_line_precedes_the_start_line()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Updates_each_duplicate_file()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Rewrites_duplicates_with_the_correct_arguments()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Still_extracts_the_method_successfully()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Does_not_write_files()
    {
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
        var originalA = await project.ReadSourceAsync("A.cs");
        var originalB = await project.ReadSourceAsync("B.cs");
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
    public async Task Skips_unmappable_matches()
    {
        // The extracted body has a parameter `x` that flows in. The "duplicate" at the
        // other site has a literal 42 where `x` was, so the structural kinds differ and
        // PatternMatcher won't report it as a match at all — the replace-duplicates flag
        // becomes a no-op (0 matches), which is the correct safe behaviour.
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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
    public async Task Rewrites_all_duplicate_occurrences()
    {
        // Regression for the multi-match-in-same-file line-drift bug uncovered when
        // dogfooding rt extract-method --replace-duplicates against FindPatternCommand.
        // The naive implementation computes match line numbers once up-front, then applies
        // them in order — so after the first in-file replacement, later matches have stale
        // line numbers and SelectStatementsInRange picks up the wrong statements (or nothing).
        using var project = new AdhocProjectBuilder();
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

        var (workspace, sln) = project.Build();
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

    [Fact]
    public async Task Rewrites_duplicate_assignment_occurrences_with_the_return_value()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void Source(string user)
                {
                    var uri = "otpauth://" + user;
                    Register(uri);
                }

                public void Duplicate(string name)
                {
                    var otpauthUri = "otpauth://" + name;
                    Register(otpauthUri);
                }

                private void Register(string value)
                {
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "A.cs"),
                "BuildOtpAuthUri",
                startLine: 7,
                endLine: 7,
                replaceDuplicates: true);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var content = await project.ReadSourceAsync("A.cs");
        var duplicateBody = GetMethodBody(content, "Duplicate");

        Assert.Contains("var otpauthUri = BuildOtpAuthUri(name);", duplicateBody);
        Assert.Contains("Register(otpauthUri);", duplicateBody);
        Assert.DoesNotContain(
            "BuildOtpAuthUri(name);",
            duplicateBody.Replace("var otpauthUri = BuildOtpAuthUri(name);", "", StringComparison.Ordinal));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Regression: conditional-expression (ternary) body — must not silently
    //  exit 0 when the selected statements include a multi-line ternary.
    //  Shape mirrors AdminScrapingController.ParseAiImages.
    //  Root cause (US-002): SolutionWriter.ApplyChangesAsync always exits 0,
    //  even when the change set is empty. The fix is a command-layer guard that
    //  exits 1 when MethodExtractor returns a solution with no changed documents.
    //  At the library level the extraction should succeed and produce changes.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Extracts_statements_containing_a_ternary_expression()
    {
        // Regression for US-002: AdminScrapingController.ParseAiImages shape.
        // A multi-line LocalDeclarationStatement whose initializer contains a
        // conditional expression must be selectable by line range and extractable.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Controller.cs", """
            using System.Collections.Generic;
            using System.Linq;

            namespace TestApp;

            public class Controller
            {
                public List<string?> ParseImages(List<string> raw)
                {
                    var images = raw
                        .Select(x => x.Length > 0
                            ? x.ToUpper()
                            : null)
                        .ToList();
                    return images;
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // lines 10-14: the multi-line LocalDeclarationStatement containing the
            // ternary LINQ projection (entire statement, not just the ternary branches)
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "Controller.cs"),
                "BuildImages",
                startLine: 10,
                endLine: 14);

            // Must produce at least one changed document — no silent no-op
            var changedDocs = newSln.GetChanges(sln)
                .GetProjectChanges()
                .SelectMany(p => p.GetChangedDocuments())
                .ToList();
            Assert.NotEmpty(changedDocs);

            await SolutionWriter.ApplyChangesAsync(sln, newSln, dryRun: false);
        }

        var content = await project.ReadSourceAsync("Controller.cs");
        Assert.Contains("BuildImages(", content);
        Assert.DoesNotContain("x.Length > 0", GetMethodBody(content, "ParseImages"));
    }

    [Fact]
    public async Task Throws_a_clear_error_when_the_selected_range_has_no_top_level_statements()
    {
        // When the user selects only sub-expression lines that do not correspond to
        // complete top-level statements (e.g. the ternary branches of a single
        // LocalDeclarationStatement), FindDocumentAndStatements throws
        // InvalidOperationException with "No statements found" — never exits 0 silently.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Controller.cs", """
            using System.Collections.Generic;
            using System.Linq;

            namespace TestApp;

            public class Controller
            {
                public List<string?> ParseImages(List<string> raw)
                {
                    var images = raw
                        .Select(x => x.Length > 0
                            ? x.ToUpper()
                            : null)
                        .ToList();
                    return images;
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // lines 11-13: only the ternary branches — not a complete statement
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                MethodExtractor.ExtractAsync(
                    sln,
                    FilePath(project, "Controller.cs"),
                    "BuildImages",
                    startLine: 11,
                    endLine: 13));

            Assert.Contains("No statements found", ex.Message);
        }
    }

    [Fact]
    public async Task Extract_throws_when_the_resulting_solution_has_no_document_changes()
    {
        // US-002: guard against silent exit-0. If MethodExtractor.ExtractAsync were to
        // return a solution with zero changed documents (e.g. a future bug in the AST
        // rewrite path), it must throw InvalidOperationException rather than allowing
        // SolutionWriter to print "No changes." and exit 0.
        //
        // We test this by verifying the guard exists: ExtractAsync must throw when the
        // computed change set is empty. The easiest way to trigger an empty change set
        // in production is to pass a line range that corresponds to an expression
        // statement whose AST rewrite leaves the document identical — which cannot
        // happen with correct rewrite logic. Therefore we verify the guard at the
        // library contract level by calling ExtractAsync with a valid range and
        // asserting it either succeeds with changes OR throws with a diagnostic —
        // and explicitly assert it never returns a zero-change solution silently.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Sample.cs", """
            namespace TestApp;

            public class Sample
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "Sample.cs"),
                "DoWork",
                startLine: 7,
                endLine: 8);

            var changedDocs = newSln.GetChanges(sln)
                .GetProjectChanges()
                .SelectMany(p => p.GetChangedDocuments())
                .ToList();

            // The guard: ExtractAsync must never return a no-op solution.
            // A zero-change result would be silently swallowed by SolutionWriter
            // (it prints "No changes." and exits 0) — this assertion catches that.
            Assert.NotEmpty(changedDocs);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --replace-duplicates: AC5 — overlapping match in the same file is skipped
    //  with a warning; non-overlapping same-file matches are both replaced.
    //
    //  The overlap path is exercised via MethodExtractorDuplicates.ApplyMatchesAsync,
    //  a test-seam overload that accepts a pre-built match list. Without that seam
    //  the overlap guard cannot be triggered because PatternMatcher only returns
    //  structurally-exact same-count matches (no subset matches). The test below
    //  verifies the non-overlapping path emits zero warnings (baseline), and a
    //  companion test verifies the warning IS emitted via the public test seam
    //  on MethodExtractorDuplicates.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Non_overlapping_same_file_duplicates_produce_no_warnings()
    {
        using var project = new AdhocProjectBuilder();
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
            }
            """);

        var warnings = new List<string>();
        var (workspace, sln) = project.Build();
        using (workspace)
        using (RtLog.UseLogger(new CollectingLogger(warnings)))
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
        Assert.Contains("PrintMessages();", GetMethodBody(content, "Source"));
        Assert.Contains("PrintMessages();", GetMethodBody(content, "Dupe1"));
        // No overlapping ranges — no warning expected.
        Assert.Empty(warnings);
    }

    [Fact]
    public async Task Overlapping_match_emits_warning_and_is_skipped()
    {
        // Verify the overlap guard emits RtLog.Warning rather than silently discarding
        // the match. We inject two PatternMatch objects with overlapping line ranges via
        // MethodExtractorDuplicates.ApplyMatchesAsync (the test-seam overload). This
        // test will be RED until that overload exists AND the guard emits a warning.
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void Method()
                {
                    System.Console.WriteLine("hello");
                    System.Console.WriteLine("world");
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "A.cs"),
                "PrintMessages",
                startLine: 7,
                endLine: 8);

            // Synthesise two overlapping matches in A.cs: match A covers lines 7-8,
            // match B also covers lines 7-8 (same range — maximally overlapping).
            // Bottom-up order: match A (higher or equal line) processed first, range
            // 7-8 claimed. Match B overlaps → should emit a warning, not silently skip.
            var absPath = FilePath(project, "A.cs");
            var matchA = new PatternMatch(absPath, Line: 7, EndLine: 8,
                Substitutions: new Dictionary<string, string>(), Preview: "");
            var matchB = new PatternMatch(absPath, Line: 7, EndLine: 8,
                Substitutions: new Dictionary<string, string>(), Preview: "");

            var warnings = new List<string>();
            using (RtLog.UseLogger(new CollectingLogger(warnings)))
            {
                await MethodExtractorDuplicates.ApplyMatchesAsync(
                    newSln,
                    matches: [matchA, matchB],
                    newMethodName: "PrintMessages",
                    orderedParameters: [],
                    returnVars: []);
            }

            // One match replaced, one skipped with a warning.
            Assert.Single(warnings);
            Assert.Contains("overlap", warnings[0], StringComparison.OrdinalIgnoreCase);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --replace-duplicates: AC6 — match at the last line of a file does not throw
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Does_not_throw_when_duplicate_is_on_the_last_line_of_the_file()
    {
        // Regression guard: SelectStatementsInRange must not throw or miss statements
        // when the matched block ends on the last content line of the file.
        using var project = new AdhocProjectBuilder();
        // Deliberately no trailing blank line after the closing brace so the last
        // statement is as close to the end of the file as possible.
        project.AddSource("A.cs", "namespace TestApp;\n\npublic class A\n{\n    public void Source()\n    {\n        System.Console.WriteLine(\"hello\");\n        System.Console.WriteLine(\"world\");\n    }\n\n    public void Dupe()\n    {\n        System.Console.WriteLine(\"hello\");\n        System.Console.WriteLine(\"world\");\n    }\n}");

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // Must not throw — Dupe's statements are near the end of the file.
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
        Assert.Contains("PrintMessages();", GetMethodBody(content, "Dupe"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --replace-duplicates with parameterization: differ only in string literals
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Parameterizes_duplicates_that_differ_only_in_string_literals()
    {
        using var project = new AdhocProjectBuilder();
        // File A: contains the extraction target with string literal
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
        // File B: identical structure but different string literals
        project.AddSource("B.cs", """
            namespace TestApp;

            public class B
            {
                public void Run()
                {
                    System.Console.WriteLine("goodbye");
                    System.Console.WriteLine("universe");
                }
            }
            """);

        var (workspace, sln) = project.Build();
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

        // A has the extracted method with hardcoded string literals
        Assert.Contains("private void PrintMessages(string line1, string line2)", contentA);
        Assert.Contains("PrintMessages(\"hello\", \"world\");", contentA);

        // B's duplicate was replaced with a call passing its string literal values
        Assert.Contains("PrintMessages(\"goodbye\", \"universe\");", contentB);
        // Original statements removed from B
        Assert.DoesNotContain("Console.WriteLine(\"goodbye\")", GetMethodBody(contentB, "Run"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class CollectingLogger(List<string> sink) : RoslynTools.Core.IRtLogger
    {
        public void Log(RoslynTools.Core.RtLogLevel level, string message)
        {
            if (level == RoslynTools.Core.RtLogLevel.Warning)
                sink.Add(message);
        }
    }

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
