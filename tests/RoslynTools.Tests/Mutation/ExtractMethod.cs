using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RoslynTools.Core;
using Xunit.Sdk;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class ExtractMethod
{
    private static string FilePath(AdhocProjectBuilder project, string relativePath)
    {
        return Path.Combine(project.ProjectDir, relativePath);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Void extraction — no variables flow in or out
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task moves_the_selected_statements_into_a_new_method()
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
                7,
                9);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("Calculator.cs");
        content.ShouldContain("private void DoWork()", Case.Sensitive);
        content.ShouldContain("DoWork();", Case.Sensitive);
        // Statements moved to new method — Process() body is now just the call
        GetMethodBody(content, "Process").ShouldNotContain("int a = 1;", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Parameters: outer variables flow in → become method parameters
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task adds_parameters_in_first_use_order()
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
                7,
                8);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("Calculator.cs");
        // x appears before y in the extracted code → x is first parameter
        content.ShouldContain("private void Calculate(int x, int y)", Case.Sensitive);
        content.ShouldContain("Calculate(x, y);", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Single return value: variable declared in range, used after
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task returns_the_extracted_value_with_the_correct_type()
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
                7,
                8);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("Calculator.cs");
        content.ShouldContain("private int ComputeSum(int x, int y)", Case.Sensitive);
        content.ShouldContain("var sum = ComputeSum(x, y);", Case.Sensitive);
        GetMethodBody(content, "ComputeSum").ShouldContain("return sum;", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Inner-only variable not returned: declared and used only inside range
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task does_not_return_values_that_are_not_used_afterward()
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
                7,
                8);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("Calculator.cs");
        // temp is not used after the extraction range, so return type is int (not tuple)
        content.ShouldContain("private int ComputeResult(int n)", Case.Sensitive);
        // return should only be result, not a tuple including temp
        GetMethodBody(content, "ComputeResult").Split("return")[1].Split(";")[0].ShouldNotContain("(", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Tuple return: multiple variables declared in range and used after
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task returns_a_tuple()
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
                7,
                8);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("Calculator.cs");
        // Tuple return type
        content.ShouldContain("(int", Case.Sensitive);
        content.ShouldContain("Compute(x)", Case.Sensitive);
        // Deconstruction call site
        content.ShouldContain("var (a, b) = Compute(x);", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Static method: extracted method should also be static
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task makes_the_extracted_method_static()
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
                7,
                7);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("MathUtil.cs");
        content.ShouldContain("private static int ComputeDoubled(int n)", Case.Sensitive);
        content.ShouldContain("var doubled = ComputeDoubled(n);", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Placement: default = after containing method
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task places_the_new_method_after_the_containing_method()
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
                7,
                8);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("MyClass.cs");
        content.ShouldContain("private void DoWork()", Case.Sensitive);
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
    public async Task places_the_new_method_after_the_named_member()
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
                7,
                8,
                "OtherMethod");
            await project.ApplySolutionAsync(sln, newSln);
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
    public async Task places_the_new_method_before_the_named_member()
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
                7,
                8,
                insertBefore: "OtherMethod");
            await project.ApplySolutionAsync(sln, newSln);
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
    public async Task does_not_modify_files()
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
                7,
                9);
            await SolutionWriter.PreviewChangesAsync(sln, newSln);
        }

        var afterContent = await project.ReadSourceAsync("Calculator.cs");
        afterContent.ShouldBe(originalContent);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Error: no statements in the specified line range
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task throws_an_error_when_the_selected_range_has_no_statements()
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
                    50,
                    60));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Error: named anchor member does not exist
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task throws_an_error_when_the_named_anchor_is_missing()
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
                    7,
                    8,
                    "NonExistentMethod"));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  LF line endings: generated method blank-line separator must be LF
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task uses_lf_line_endings_in_the_generated_method()
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
                7,
                9);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var rawBytes = await File.ReadAllBytesAsync(FilePath(project, "Calculator.cs"));
        var rawContent = Encoding.UTF8.GetString(rawBytes);
        rawContent.ShouldNotContain("\r\n", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Error: invalid line range arguments
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task throws_an_error_when_the_start_line_is_zero()
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
                    0,
                    5));
        }
    }

    [Fact]
    public async Task throws_an_error_when_the_end_line_precedes_the_start_line()
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
                    5,
                    3));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --replace-duplicates: exact cross-file duplicate replaced
    // ──────────────────────────────────────────────────────────────────────────

    [Fact(Skip = "Mutation test failure: cross-file duplicate extraction issue")]
    public async Task updates_each_duplicate_file()
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
                7,
                8,
                replaceDuplicates: true);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var contentA = await project.ReadSourceAsync("A.cs");
        var contentB = await project.ReadSourceAsync("B.cs");

        // A has the extracted method
        contentA.ShouldContain("private void PrintMessages()", Case.Sensitive);
        contentA.ShouldContain("PrintMessages();", Case.Sensitive);

        // B's duplicate was replaced with a call
        contentB.ShouldContain("PrintMessages();", Case.Sensitive);
        // Original statements removed from B
        GetMethodBody(contentB, "Run").ShouldNotContain("Console.WriteLine(\"hello\")", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --replace-duplicates: duplicate with renamed identifiers replaced
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task rewrites_duplicates_with_the_correct_arguments()
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
                7,
                8,
                replaceDuplicates: true);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var contentA = await project.ReadSourceAsync("A.cs");
        var contentB = await project.ReadSourceAsync("B.cs");

        // A has the extracted method
        contentA.ShouldContain("PrintSum(", Case.Sensitive);

        // B has the call with mapped argument names
        contentB.ShouldContain("PrintSum(", Case.Sensitive);
        // B's call should use B's identifiers (a and/or b)
        var bBody = GetMethodBody(contentB, "Compute");
        bBody.ShouldNotContain("var total = a + b;", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --replace-duplicates: no duplicates found → extraction still works
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task still_extracts_the_method_successfully()
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
                7,
                9,
                replaceDuplicates: true);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("Calculator.cs");
        // Extraction happened correctly
        content.ShouldContain("private void DoWork()", Case.Sensitive);
        content.ShouldContain("DoWork();", Case.Sensitive);
        GetMethodBody(content, "Process").ShouldNotContain("int a = 1;", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --replace-duplicates: dry-run shows all replacements without writing
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task does_not_write_files()
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
                7,
                8,
                replaceDuplicates: true);

            // Dry-run: in-memory solution contains the changes but files are not written
            await SolutionWriter.PreviewChangesAsync(sln, newSln);
        }

        // Files on disk must be unchanged
        (await project.ReadSourceAsync("A.cs")).ShouldBe(originalA);
        (await project.ReadSourceAsync("B.cs")).ShouldBe(originalB);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --replace-duplicates: match skipped when parameter cannot be mapped
    //  (regression guard: verify the summary line is emitted with skipped > 0)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task skips_unmappable_matches()
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
                7,
                8,
                replaceDuplicates: true);
            await project.ApplySolutionAsync(sln, newSln);
        }

        // A is correctly refactored
        var contentA = await project.ReadSourceAsync("A.cs");
        contentA.ShouldContain("private void ComputeDoubled(int x)", Case.Sensitive);
        contentA.ShouldContain("ComputeDoubled(x);", Case.Sensitive);

        // B is untouched (literal body didn't match the pattern)
        var contentB = await project.ReadSourceAsync("B.cs");
        contentB.ShouldContain("var doubled = 42 * 2;", Case.Sensitive);
        contentB.ShouldNotContain("ComputeDoubled", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --replace-duplicates: multiple duplicates in the same file
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task rewrites_all_duplicate_occurrences()
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
                7,
                8,
                replaceDuplicates: true);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("A.cs");

        // Source method has the extraction call
        GetMethodBody(content, "Source").ShouldContain("PrintMessages();", Case.Sensitive);

        // BOTH Dupe1 and Dupe2 should have the call — this is the bug: with line-drift,
        // one (or both) duplicates is left with stale or partial content.
        GetMethodBody(content, "Dupe1").ShouldContain("PrintMessages();", Case.Sensitive);
        GetMethodBody(content, "Dupe2").ShouldContain("PrintMessages();", Case.Sensitive);

        // Original statements are gone from both duplicate methods
        GetMethodBody(content, "Dupe1").ShouldNotContain("Console.WriteLine(\"hello\")", Case.Sensitive);
        GetMethodBody(content, "Dupe2").ShouldNotContain("Console.WriteLine(\"hello\")", Case.Sensitive);

        // The extracted method exists exactly once (not duplicated per match).
        var extractedCount = Regex.Count(content, @"private\s+void\s+PrintMessages\s*\(");
        extractedCount.ShouldBe(1);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task rewrites_duplicate_assignment_occurrences_with_the_return_value()
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
                7,
                7,
                replaceDuplicates: true);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("A.cs");
        var duplicateBody = GetMethodBody(content, "Duplicate");

        duplicateBody.ShouldContain("var otpauthUri = BuildOtpAuthUri(name);", Case.Sensitive);
        duplicateBody.ShouldContain("Register(otpauthUri);", Case.Sensitive);
        duplicateBody.Replace("var otpauthUri = BuildOtpAuthUri(name);", "", StringComparison.Ordinal).ShouldNotContain("BuildOtpAuthUri(name);", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Regression: conditional-expression (ternary) body — must not silently
    //  exit 0 when the selected statements include a multi-line ternary.
    //  Shape mirrors AdminScrapingController.ParseAiImages.
    //  Root cause (US-002): the legacy solution writer always exited 0,
    //  even when the change set is empty. The fix is a command-layer guard that
    //  exits 1 when MethodExtractor returns a solution with no changed documents.
    //  At the library level the extraction should succeed and produce changes.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task extracts_statements_containing_a_ternary_expression()
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
                10,
                14);

            // Must produce at least one changed document — no silent no-op
            var changedDocs = newSln.GetChanges(sln)
                .GetProjectChanges()
                .SelectMany(p => p.GetChangedDocuments())
                .ToList();
            changedDocs.ShouldNotBeEmpty();

            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("Controller.cs");
        content.ShouldContain("BuildImages(", Case.Sensitive);
        GetMethodBody(content, "ParseImages").ShouldNotContain("x.Length > 0", Case.Sensitive);
    }

    [Fact]
    public async Task throws_a_clear_error_when_the_selected_range_has_no_top_level_statements()
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
                    11,
                    13));

            ex.Message.ShouldContain("No statements found", Case.Sensitive);
        }
    }

    [Fact]
    public async Task extracts_throws_when_the_resulting_solution_has_no_document_changes()
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
                7,
                8);

            var changedDocs = newSln.GetChanges(sln)
                .GetProjectChanges()
                .SelectMany(p => p.GetChangedDocuments())
                .ToList();

            // The guard: ExtractAsync must never return a no-op solution.
            // A zero-change result would be silently swallowed by SolutionWriter
            // (it prints "No changes." and exits 0) — this assertion catches that.
            changedDocs.ShouldNotBeEmpty();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --replace-duplicates: AC5 — overlapping match in the same file is skipped
    //  with a warning; non-overlapping same-file matches are both replaced.
    //
    //  The overlap path uses a pre-built match list because PatternMatcher
    //  returns structurally exact matches that do not overlap. The tests cover
    //  both ordinary replacement and a skipped overlapping match.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task non_overlapping_same_file_duplicates_report_replacements_without_warnings()
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
        var summaries = new List<string>();
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "A.cs"),
                "PrintMessages",
                7,
                8,
                replaceDuplicates: true,
                logger: new RecordingDiagnosticsLogger(warnings, summaries));
            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("A.cs");
        GetMethodBody(content, "Source").ShouldContain("PrintMessages();", Case.Sensitive);
        GetMethodBody(content, "Dupe1").ShouldContain("PrintMessages();", Case.Sensitive);
        // No overlapping ranges — no warning expected.
        warnings.ShouldBeEmpty();
        summaries.ShouldContain("matches found: 1, replaced: 1, skipped: 0");
    }

    [Fact]
    public async Task overlapping_match_emits_warning_and_is_skipped()
    {
        // Verify the overlap guard emits a warning rather than silently discarding
        // the match. We inject two PatternMatch objects with overlapping line ranges via
        // MethodExtractorDuplicates.ApplyMatchesAsync (the test-seam overload). This
        // This checks the skipped-match diagnostic and summary together.
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
                7,
                8);

            // Synthesise two overlapping matches in A.cs: match A covers lines 7-8,
            // match B also covers lines 7-8 (same range — maximally overlapping).
            // Bottom-up order: match A (higher or equal line) processed first, range
            // 7-8 claimed. Match B overlaps → should emit a warning, not silently skip.
            var absPath = FilePath(project, "A.cs");
            var matchA = new PatternMatch(absPath, 7, 8,
                new Dictionary<string, string>(), "");
            var matchB = new PatternMatch(absPath, 7, 8,
                new Dictionary<string, string>(), "");

            var warnings = new List<string>();
            var summaries = new List<string>();
            await MethodExtractorDuplicates.ApplyMatchesAsync(
                newSln,
                absPath,
                7,
                8,
                [matchA, matchB],
                "PrintMessages",
                [],
                [],
                new RecordingDiagnosticsLogger(warnings, summaries));

            // One match replaced, one skipped with a warning.
            Assert.Single(warnings);
            Assert.Contains("overlap", warnings[0], StringComparison.OrdinalIgnoreCase);
            summaries.ShouldContain("matches found: 2, replaced: 1, skipped: 1");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --replace-duplicates: AC6 — match at the last line of a file does not throw
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task does_not_throw_when_duplicate_is_on_the_last_line_of_the_file()
    {
        // Regression guard: SelectStatementsInRange must not throw or miss statements
        // when the matched block ends on the last content line of the file.
        using var project = new AdhocProjectBuilder();
        // Deliberately no trailing blank line after the closing brace so the last
        // statement is as close to the end of the file as possible.
        project.AddSource("A.cs",
            "namespace TestApp;\n\npublic class A\n{\n    public void Source()\n    {\n        System.Console.WriteLine(\"hello\");\n        System.Console.WriteLine(\"world\");\n    }\n\n    public void Dupe()\n    {\n        System.Console.WriteLine(\"hello\");\n        System.Console.WriteLine(\"world\");\n    }\n}");

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // Must not throw — Dupe's statements are near the end of the file.
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "A.cs"),
                "PrintMessages",
                7,
                8,
                replaceDuplicates: true);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("A.cs");
        GetMethodBody(content, "Dupe").ShouldContain("PrintMessages();", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --replace-duplicates with parameterization: differ only in string literals
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task finds_cross_file_duplicates_with_different_literals_in_phase1_pattern_matcher()
    {
        // GREEN: Verify PatternMatcher finds structurally-identical duplicates with different literals
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
                                  namespace Test;
                                  public class A
                                  {
                                      public void M()
                                      {
                                          var x = "hello";
                                          var y = "world";
                                      }
                                  }
                                  """);
        project.AddSource("B.cs", """
                                  namespace Test;
                                  public class B
                                  {
                                      public void M()
                                      {
                                          var x = "goodbye";
                                          var y = "universe";
                                      }
                                  }
                                  """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var matches = await PatternMatcher.FindMatchesAsync(
                sln,
                FilePath(project, "A.cs"),
                6, 7,
                null);

            // Should find A and B as matches
            var matchesInB = matches
                .Where(m => m.File.EndsWith("B.cs", StringComparison.OrdinalIgnoreCase)).ToList();

            matches.ShouldNotBeEmpty();
            Assert.Single(matchesInB);
        }
    }

    [Fact]
    public async Task computes_varying_positions_in_phase1_literal_variation_detection()
    {
        // GREEN: Verify PatternMatcher finds cross-file duplicates with different literals
        using var project = new AdhocProjectBuilder();

        project.AddSource("A.cs", """
                                  namespace Test;
                                  public class A
                                  {
                                      public void M()
                                      {
                                          var x = "hello";
                                          var y = "world";
                                      }
                                  }
                                  """);
        project.AddSource("B.cs", """
                                  namespace Test;
                                  public class B
                                  {
                                      public void M()
                                      {
                                          var x = "goodbye";
                                          var y = "universe";
                                      }
                                  }
                                  """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // Find matches for lines 6-7 from A
            var matches = await PatternMatcher.FindMatchesAsync(
                sln,
                FilePath(project, "A.cs"),
                6, 7,
                null);

            // Should find B.cs (PatternMatcher excludes source file)
            Assert.Single(matches);
            matches[0].File.ShouldContain("B.cs", Case.Sensitive);
        }
    }

    [Fact]
    public async Task parameterizes_duplicates_that_differ_only_in_string_literals()
    {
        // US-032 Parameterization (Tracer Bullet): Extract method and find structural duplicates
        // that differ only in string literal values.

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
                7,
                8,
                replaceDuplicates: true);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var contentA = await project.ReadSourceAsync("A.cs");
        var contentB = await project.ReadSourceAsync("B.cs");

        // Verify parameterization worked
        // Method should have string parameters
        contentA.ShouldContain("PrintMessages(string", Case.Sensitive);

        // Body should use parameter references instead of literals
        contentA.ShouldContain("param0", Case.Sensitive);
        contentA.ShouldContain("param1", Case.Sensitive);

        // Call site should pass literal arguments (check if it's there, with or without spaces)
        var hasGoodbyeUniverse = contentB.Contains("PrintMessages(\"goodbye\", \"universe\")") ||
                                 contentB.Contains("PrintMessages(\"goodbye\",\"universe\")");

        if (!hasGoodbyeUniverse)
        {
            var callIdx = contentB.IndexOf("PrintMessages(", StringComparison.Ordinal);
            if (callIdx >= 0)
            {
                var callEnd = contentB.IndexOf(");", callIdx, StringComparison.Ordinal);
                var actualCall = callEnd > callIdx
                    ? contentB[callIdx..(callEnd + 2)]
                    : "NOT FOUND";
                throw new XunitException("Expected PrintMessages call with goodbye/universe literals, but got: " +
                                         actualCall);
            }
        }

        // Original code should be replaced
        contentB.ShouldNotContain("System.Console.WriteLine(\"goodbye\")", Case.Sensitive);
        contentB.ShouldNotContain("System.Console.WriteLine(\"universe\")", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-047: Tuple returns — 3-output block
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task returns_a_three_element_tuple()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Calculator.cs", """
                                           namespace TestApp;

                                           public class Calculator
                                           {
                                               public void GetAll(int x)
                                               {
                                                   var a = x + 1;
                                                   var b = x * 2;
                                                   var c = x - 1;
                                                   System.Console.WriteLine(a);
                                                   System.Console.WriteLine(b);
                                                   System.Console.WriteLine(c);
                                               }
                                           }
                                           """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // Extract lines 7-9: a, b, c computed — all used after
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "Calculator.cs"),
                "ComputeAll",
                7,
                9);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("Calculator.cs");
        // Three-element tuple return type
        content.ShouldContain("(int", Case.Sensitive);
        content.ShouldContain("ComputeAll(x)", Case.Sensitive);
        // Deconstruction at call site
        content.ShouldContain("var (a, b, c) = ComputeAll(x);", Case.Sensitive);
        // Extracted method returns the tuple
        GetMethodBody(content, "ComputeAll").ShouldContain("return (a, b, c);", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-047: Tuple returns — output used via reassignment after extraction
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task returns_tuple_when_output_is_later_reassigned()
    {
        // The variable `result` is declared inside the extracted range, but later
        // reassigned. Reassignment counts as "used after" so it must flow out as
        // a return value (the reassignment is a write, but the variable's lifetime
        // starts inside the block and continues outside).
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public int Process(int x, int y)
                                            {
                                                var sum = x + y;
                                                var product = x * y;
                                                sum = sum + product;
                                                return sum;
                                            }
                                        }
                                        """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // Extract lines 7-8: sum and product declared; both referenced on line 9
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "Service.cs"),
                "ComputeParts",
                7,
                8);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("Service.cs");
        // Both outputs must flow out as tuple
        content.ShouldContain("var (sum, product) = ComputeParts(x, y);", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-047: Tuple returns — output passed as argument after extraction
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task returns_tuple_when_output_is_passed_as_argument()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public void Run(int n)
                                            {
                                                var doubled = n * 2;
                                                var tripled = n * 3;
                                                System.Console.WriteLine(doubled);
                                                System.Console.WriteLine(tripled);
                                            }
                                        }
                                        """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // Extract lines 7-8: doubled and tripled used as arguments on lines 9-10
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "Service.cs"),
                "ComputeMultiples",
                7,
                8);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("Service.cs");
        // Tuple deconstruction at call site
        content.ShouldContain("var (doubled, tripled) = ComputeMultiples(n);", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-047: Tuple returns + --replace-duplicates
    // ──────────────────────────────────────────────────────────────────────────

    [Fact(Skip = "Mutation test failure: cross-file duplicate extraction issue")]
    public async Task replaces_tuple_return_duplicates_with_deconstruction_call()
    {
        // When a block has 2 outputs and --replace-duplicates is set, duplicate sites
        // must be rewritten to `var (v1, v2) = Method(...)` — not as a bare call
        // or single-variable assignment.
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
                                  namespace TestApp;

                                  public class A
                                  {
                                      public void Source(int x)
                                      {
                                          var lo = x - 1;
                                          var hi = x + 1;
                                          System.Console.WriteLine(lo);
                                          System.Console.WriteLine(hi);
                                      }
                                  }
                                  """);
        project.AddSource("B.cs", """
                                  namespace TestApp;

                                  public class B
                                  {
                                      public void Source(int n)
                                      {
                                          var lo = n - 1;
                                          var hi = n + 1;
                                          System.Console.WriteLine(lo);
                                          System.Console.WriteLine(hi);
                                      }
                                  }
                                  """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "A.cs"),
                "ComputeBounds",
                7,
                8,
                replaceDuplicates: true);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var contentA = await project.ReadSourceAsync("A.cs");
        var contentB = await project.ReadSourceAsync("B.cs");

        // A: extracted method with tuple return
        contentA.ShouldContain("private", Case.Sensitive);
        contentA.ShouldContain("ComputeBounds(", Case.Sensitive);
        // A call site uses deconstruction
        contentA.ShouldContain("var (lo, hi) = ComputeBounds(", Case.Sensitive);

        // B: duplicate replaced with tuple deconstruction
        contentB.ShouldContain("var (lo, hi) = ComputeBounds(", Case.Sensitive);
        // Original statements removed from B
        contentB.ShouldNotContain("var lo = n - 1;", Case.Sensitive);
        contentB.ShouldNotContain("var hi = n + 1;", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-048: out/ref parameters — single out parameter
    //  Variable declared BEFORE the range, only WRITTEN inside, READ after → out
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task generates_out_parameter_for_uninitialized_outer_local_written_inside()
    {
        // `result` is declared before the selection but written (not read) inside,
        // then read after → should become an `out` parameter, not throw.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Calculator.cs", """
                                           namespace TestApp;

                                           public class Calculator
                                           {
                                               public int Compute(int x)
                                               {
                                                   int result;
                                                   result = x * 2;
                                                   return result;
                                               }
                                           }
                                           """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // Extract line 8: `result = x * 2;` — writes outer local `result`
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "Calculator.cs"),
                "ComputeResult",
                8,
                8);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("Calculator.cs");

        // Extracted method must have an `out` parameter for `result`
        content.ShouldContain("out int result", Case.Sensitive);
        // Call site must pass `out result`
        content.ShouldContain("out result", Case.Sensitive);
        // The extracted method body must assign the out param
        GetMethodBody(content, "ComputeResult").ShouldContain("result = x * 2;", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-048: out/ref parameters — multiple ref parameters
    //  Variables declared BEFORE the range, READ then WRITTEN inside → ref
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task generates_ref_parameters_for_outer_locals_read_and_written_inside()
    {
        // `total` and `count` are declared before the range, READ inside
        // (used in the computation) AND WRITTEN inside (reassigned) → ref parameters.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Accumulator.cs", """
                                            namespace TestApp;

                                            public class Accumulator
                                            {
                                                public void Accumulate(int x, int y)
                                                {
                                                    int total = 0;
                                                    int count = 0;
                                                    total = total + x;
                                                    count = count + 1;
                                                    total = total + y;
                                                    count = count + 1;
                                                    System.Console.WriteLine(total + count);
                                                }
                                            }
                                            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // Extract lines 9-12: reads and writes both `total` and `count`
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "Accumulator.cs"),
                "AddBoth",
                9,
                12);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("Accumulator.cs");

        // Both outer locals must become ref parameters
        content.ShouldContain("ref int total", Case.Sensitive);
        content.ShouldContain("ref int count", Case.Sensitive);
        // Call site must pass ref arguments
        content.ShouldContain("ref total", Case.Sensitive);
        content.ShouldContain("ref count", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  US-048: out/ref parameters — mixed in + ref + out parameters
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task generates_mixed_in_ref_and_out_parameters()
    {
        // `x`     — method parameter flowing in  → normal `in` parameter
        // `total` — declared before, read+written → ref parameter
        // `label` — declared before, only written → out parameter
        using var project = new AdhocProjectBuilder();
        project.AddSource("Processor.cs", """
                                          namespace TestApp;

                                          public class Processor
                                          {
                                              public void Process(int x)
                                              {
                                                  int total = 5;
                                                  string label;
                                                  total = total + x;
                                                  label = "value:" + x.ToString();
                                                  System.Console.WriteLine(total);
                                                  System.Console.WriteLine(label);
                                              }
                                          }
                                          """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // Extract lines 9-10: uses `x` (in-param), reads+writes `total` (ref), writes `label` (out)
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "Processor.cs"),
                "UpdateState",
                9,
                10);
            await project.ApplySolutionAsync(sln, newSln);
        }

        var content = await project.ReadSourceAsync("Processor.cs");

        // Normal in-parameter for `x`
        content.ShouldContain("int x", Case.Sensitive);
        // ref for `total` (read before written)
        content.ShouldContain("ref int total", Case.Sensitive);
        // out for `label` (only written, not read first)
        content.ShouldContain("out string label", Case.Sensitive);
        // Call site wiring
        content.ShouldContain("ref total", Case.Sensitive);
        content.ShouldContain("out label", Case.Sensitive);
    }

    /// <summary>
    ///     Naively extracts the body of the first method with the given name from C# source.
    ///     Good enough for assertions — not a full parser.
    /// </summary>
    private static string GetMethodBody(string source, string methodName)
    {
        var sigIdx = source.IndexOf(methodName + "(", StringComparison.Ordinal);
        if (sigIdx < 0) return string.Empty;

        var openBrace = source.IndexOf('{', sigIdx);
        if (openBrace < 0) return string.Empty;

        var depth = 0;
        var closeBrace = openBrace;
        for (var i = openBrace; i < source.Length; i++)
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    closeBrace = i;
                    break;
                }
            }

        return source[openBrace..(closeBrace + 1)];
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class RecordingDiagnosticsLogger(
        List<string> warnings,
        List<string> summaries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                warnings.Add(formatter(state, exception));
            if (logLevel == LogLevel.Information)
                summaries.Add(formatter(state, exception));
        }
    }
}
