using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public class Complexity : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public Complexity(RtServicesFixture services) => _services = services;

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<ComplexityReport> AnalyzeSourceAsync(
        string source,
        int threshold = 0,
        string sort = "cyclomatic",
        int top = 0,
        string fileName = "Test.cs")
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource(fileName, source);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            return await ComplexityAnalyzer.AnalyzeAsync(sln, threshold, sort, top);
        }
    }

    // Matches by exact FQN or by the suffix after the last dot (e.g. "M" matches "C.M")
    private static MethodComplexity GetMethod(ComplexityReport report, string name)
    {
        return report.Methods.First(m => m.Method == name || m.Method.EndsWith("." + name, StringComparison.Ordinal));
    }

    private static MethodComplexity? FindMethod(ComplexityReport report, string name)
    {
        return report.Methods.FirstOrDefault(m =>
            m.Method == name || m.Method.EndsWith("." + name, StringComparison.Ordinal));
    }

    // ── Cyclomatic complexity ───────────────────────────────────────────────
    //
    // Each row exercises one decision-point category. Base count is 1; each
    // branching construct adds 1. Notably absent: bare `else`, `default:` in
    // switch statement, and `_` arm in switch expression (none add).

    [Theory]
    [InlineData("base: no branches",
        """public class C { public void M() { var x = 1; } }""", 1)]
    [InlineData("if without else adds 1; else does NOT add",
        """public class C { public void M(bool b) { if (b) { var x = 1; } else { var y = 2; } } }""", 2)]
    [InlineData("for, foreach, while, do each add 1",
        """public class C { public void M(int[] arr) { for (int i = 0; i < 10; i++) { } foreach (var x in arr) { } while (true) { break; } do { break; } while (false); } }""", 5)]
    [InlineData("each catch clause adds 1",
        """public class C { public void M() { try { } catch (System.Exception) { } catch (System.InvalidOperationException) { } } }""", 3)]
    [InlineData("ternary, ??, ?. each add 1",
        """public class C { public string? M(bool b, string? s, string? t) { var x = b ? "a" : "b"; var y = s ?? t; var z = s?.Length; return x; } }""", 4)]
    [InlineData("&& and || each add 1",
        """public class C { public bool M(bool a, bool b, bool c) { return a && b || c; } }""", 3)]
    [InlineData("??= adds 1",
        """public class C { public void M(string? s) { s ??= "default"; } }""", 2)]
    [InlineData("switch statement: non-default case labels each add 1",
        """public class C { public void M(int x) { switch (x) { case 1: break; case 2: break; default: break; } } }""", 3)]
    [InlineData("switch expression: non-discard arms each add 1",
        """public class C { public string M(int x) => x switch { 1 => "one", 2 => "two", _ => "other" }; }""", 3)]
    [InlineData("async/await is not a decision point",
        """public class C { public async System.Threading.Tasks.Task<int> M() { await System.Threading.Tasks.Task.Delay(0); return 1; } }""", 1)]
    [InlineData("yield return/break is not a decision point",
        """public class C { public System.Collections.Generic.IEnumerable<int> M() { yield return 1; yield return 2; yield break; } }""", 1)]
    public async Task cyclomatic_complexity_matches_decision_point_count(string scenario, string source, int expectedCyclomatic)
    {
        _ = scenario;
        var report = await AnalyzeSourceAsync(source);
        GetMethod(report, "M").Cyclomatic.ShouldBe(expectedCyclomatic);
    }

    // ── LOC (lines of code) ─────────────────────────────────────────────────

    [Theory]
    [InlineData("expression-bodied method counts as 1 line",
        """public class C { public int M() => 42; }""", 1)]
    [InlineData("block body excludes blank lines and braces",
        """
        public class C {
            public void M() {

                var x = 1;

                var y = 2;

            }
        }
        """, 2)]
    [InlineData("comment-only lines are excluded",
        """
        public class C {
            public void M() {
                // line comment
                var x = 1;
                /* block comment */
                var y = 2;
            }
        }
        """, 2)]
    public async Task loc_counts_statements_excluding_braces_blanks_and_comments(string scenario, string source, int expectedLoc)
    {
        _ = scenario;
        var report = await AnalyzeSourceAsync(source);
        GetMethod(report, "M").Loc.ShouldBe(expectedLoc);
    }

    // ── Params ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("regular method: all parameters counted",
        """public class C { public void M(int a, string b, bool c) { } }""", 3)]
    [InlineData("extension method: 'this' parameter excluded",
        """public static class C { public static void M(this string s, int n) { } }""", 1)]
    public async Task params_count_excludes_extension_this_parameter(string scenario, string source, int expectedParams)
    {
        _ = scenario;
        var report = await AnalyzeSourceAsync(source);
        GetMethod(report, "M").Params.ShouldBe(expectedParams);
    }

    // ── Member coverage (which method-like declarations are reported) ───────
    //
    // includedCsv / excludedCsv: comma-separated method-name suffixes the
    // analyzer must / must not include in report.Methods.

    [Theory]
    [InlineData("constructor is included",
        """public class C { public C(int x) { var y = x + 1; } }""",
        ".C", "")]
    [InlineData("property accessors with bodies are included",
        """public class C { private int _x; public int X { get { return _x; } set { _x = value; } } }""",
        ".X.get,.X.set", "")]
    [InlineData("auto-property accessors are skipped (no body)",
        """public class C { public int X { get; set; } }""",
        "", ".X.get,.X.set")]
    [InlineData("abstract method is skipped; sibling concrete method included",
        """public abstract class C { public abstract void M(); public void N() { var x = 1; } }""",
        ".N", ".M")]
    [InlineData("partial method without body is skipped; sibling included",
        """public partial class C { partial void M(); public void N() { var x = 1; } }""",
        ".N", ".M")]
    [InlineData("local function inside a method is included",
        """public class C { public void M() { int Local(int x) { return x + 1; } Local(1); } }""",
        ".Local", "")]
    public async Task member_kinds_are_included_or_skipped_per_analyzer_contract(
        string scenario,
        string source,
        string includedCsv,
        string excludedCsv)
    {
        _ = scenario;
        var report = await AnalyzeSourceAsync(source);

        foreach (var suffix in includedCsv.Split(',', StringSplitOptions.RemoveEmptyEntries))
            report.Methods.ShouldContain(m => m.Method.EndsWith(suffix, StringComparison.Ordinal));

        foreach (var suffix in excludedCsv.Split(',', StringSplitOptions.RemoveEmptyEntries))
            report.Methods.ShouldNotContain(m => m.Method.EndsWith(suffix, StringComparison.Ordinal));
    }

    // ── Generated files / generated markers are skipped ─────────────────────
    //
    // Three skip vectors: .g.cs suffix, .Designer.cs suffix, and the
    // // <auto-generated> marker comment in file content.

    [Theory]
    [InlineData("Generated.g.cs", """public class Skipped { public void M() { var x = 1; } }""")]
    [InlineData("Form1.Designer.cs", """public class Skipped { public void M() { var x = 1; } }""")]
    [InlineData("AutoGenerated.cs",
        """
        // <auto-generated>
        public class Skipped { public void M() { var x = 1; } }
        """)]
    public async Task generated_files_are_skipped_from_complexity_analysis(string fileName, string source)
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource(fileName, source);
        project.AddSource("Real.cs", """public class Real { public void N() { var x = 1; } }""");

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            report.Methods.ShouldNotContain(m => m.Type == "Skipped");
            report.Methods.ShouldContain(m => m.Type == "Real");
        }
    }

    // -----------------------------------------------------------------------
    // Threshold filter: only methods with ANY metric >= threshold included
    // -----------------------------------------------------------------------

    [Fact]
    public async Task threshold_only_methods_meeting_threshold_are_returned()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Test.cs", """
                                     public class C {
                                         public void Simple() { var x = 1; }
                                         public void Complex(bool a, bool b) {
                                             if (a) { }
                                             if (b) { }
                                             if (a && b) { }
                                         }
                                     }
                                     """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln, 4);
            // Complex has cyclomatic >= 4; Simple does not
            report.Methods.ShouldNotContain(m => m.Method.EndsWith(".Simple", StringComparison.Ordinal));
            report.Methods.ShouldContain(m => m.Method.EndsWith(".Complex", StringComparison.Ordinal));
        }
    }

    // -----------------------------------------------------------------------
    // Rollups: computed over ALL methods regardless of filter
    // -----------------------------------------------------------------------

    [Fact]
    public async Task includes_rollups_for_all_methods_regardless_of_threshold_filter()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Test.cs", """
                                     namespace MyNs;
                                     public class C {
                                         public void Simple() { var x = 1; }
                                         public void Complex(bool a, bool b) {
                                             if (a) { }
                                             if (b) { }
                                             if (a && b) { }
                                         }
                                     }
                                     """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // threshold=10 filters Methods to empty, but rollups should still have both
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln, 10);
            report.Methods.ShouldBeEmpty();
            var typeRollup = Assert.Single(report.TypeRollups);
            typeRollup.Type.ShouldBe("MyNs.C");
            typeRollup.MethodCount.ShouldBe(2);
        }
    }

    // ── Sort: each metric orders methods descending by that metric ─────────

    [Theory]
    [InlineData("cyclomatic",
        """public class C { public void Low() { var x = 1; } public void High(bool a, bool b, bool c) { if (a) { } if (b) { } if (c) { } } }""",
        ".High", ".Low")]
    [InlineData("cognitive",
        """public class C { public void Low() { var x = 1; } public void High(bool a, bool b) { if (a) { if (b) { } } } }""",
        ".High", ".Low")]
    [InlineData("loc",
        """
        public class C {
            public void Short() { var x = 1; }
            public void Long() {
                var a = 1;
                var b = 2;
                var c = 3;
                var d = 4;
                var e = 5;
            }
        }
        """,
        ".Long", ".Short")]
    [InlineData("params",
        """public class C { public void Few(int a) { } public void Many(int a, int b, int c, int d, int e) { } }""",
        ".Many", ".Few")]
    public async Task sort_orders_methods_descending_by_selected_metric(string sortKey, string source, string firstSuffix, string lastSuffix)
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Test.cs", source);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln, sort: sortKey);
            var methods = report.Methods.ToList();
            var firstIdx = methods.FindIndex(m => m.Method.EndsWith(firstSuffix, StringComparison.Ordinal));
            var lastIdx = methods.FindIndex(m => m.Method.EndsWith(lastSuffix, StringComparison.Ordinal));
            Assert.True(firstIdx < lastIdx, $"{firstSuffix} should sort before {lastSuffix} for sort '{sortKey}'");
        }
    }

    // -----------------------------------------------------------------------
    // Top: truncates results
    // -----------------------------------------------------------------------

    [Fact]
    public async Task top_results_are_truncated_to_n()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Test.cs", """
                                     public class C {
                                         public void A() { var x = 1; }
                                         public void B() { var x = 1; }
                                         public void D() { var x = 1; }
                                         public void E() { var x = 1; }
                                     }
                                     """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln, top: 2);
            report.Methods.Count.ShouldBe(2);
        }
    }

    // -----------------------------------------------------------------------
    // Rollups: type rollup aggregates correctly
    // -----------------------------------------------------------------------

    [Fact]
    public async Task handles_type_rollup_aggregates_method_metrics()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Test.cs", """
                                     namespace MyApp;
                                     public class MyClass {
                                         public void A() { var x = 1; }
                                         public void B(bool b) { if (b) { } }
                                     }
                                     """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            var rollup = Assert.Single(report.TypeRollups);
            rollup.Type.ShouldBe("MyApp.MyClass");
            rollup.Namespace.ShouldBe("MyApp");
            rollup.MethodCount.ShouldBe(2);
            // A: cyclomatic=1, B: cyclomatic=2 => total=3
            rollup.CyclomaticTotal.ShouldBe(3);
        }
    }

    // -----------------------------------------------------------------------
    // Rollups: namespace rollup aggregates correctly
    // -----------------------------------------------------------------------

    [Fact]
    public async Task handles_namespace_rollup_aggregates_across_types()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Test.cs", """
                                     namespace MyNs;
                                     public class A { public void M1() { var x = 1; } }
                                     public class B { public void M2() { var x = 1; } }
                                     """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            var nsRollup = Assert.Single(report.NamespaceRollups);
            nsRollup.Namespace.ShouldBe("MyNs");
            nsRollup.MethodCount.ShouldBe(2);
        }
    }

    // ── Cognitive complexity ────────────────────────────────────────────────

    [Theory]
    [InlineData("nested ifs: outer 1+0=1, inner 1+1=2 → 3",
        """public class C { public void M(bool a, bool b) { if (a) { if (b) { } } } }""", 3)]
    [InlineData("if/else: if 1+0=1, else +1 (no depth) → 2",
        """public class C { public void M(bool b) { if (b) { var x = 1; } else { var y = 2; } } }""", 2)]
    public async Task cognitive_complexity_respects_nesting_penalty(string scenario, string source, int expectedCognitive)
    {
        _ = scenario;
        var report = await AnalyzeSourceAsync(source);
        GetMethod(report, "M").Cognitive.ShouldBe(expectedCognitive);
    }

    // -----------------------------------------------------------------------
    // Metadata: file path and line populated
    // -----------------------------------------------------------------------

    [Fact]
    public async Task handles_method_has_file_path_and_line_populated()
    {
        var report = await AnalyzeSourceAsync("""
                                              public class C {
                                                  public void M() { var x = 1; }
                                              }
                                              """);
        var m = GetMethod(report, "M");
        string.IsNullOrEmpty(m.File).ShouldBeFalse();
        (m.Line > 0).ShouldBeTrue();
    }

    // -----------------------------------------------------------------------
    // Metadata: type and namespace populated
    // -----------------------------------------------------------------------

    [Fact]
    public async Task handles_method_has_type_and_namespace_populated()
    {
        var report = await AnalyzeSourceAsync("""
                                              namespace Foo.Bar;
                                              public class MyType {
                                                  public void M() { var x = 1; }
                                              }
                                              """);
        var m = GetMethod(report, "M");
        m.Type.ShouldBe("Foo.Bar.MyType");
        m.Namespace.ShouldBe("Foo.Bar");
    }

    // -----------------------------------------------------------------------
    // Empty project returns empty report
    // -----------------------------------------------------------------------

    [Fact]
    public async Task no_methods_report_is_empty()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Empty.cs", "// nothing here");
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            report.Methods.ShouldBeEmpty();
            report.TypeRollups.ShouldBeEmpty();
            report.NamespaceRollups.ShouldBeEmpty();
        }
    }

    // ── IRtCommand contract ─────────────────────────────────────────────────

    [Fact]
    public void command_info_is_a_read_only_analyze_command_with_json_output()
    {
        ComplexityCommand.Info.Category.ShouldBe("analyze");
        ComplexityCommand.Info.JsonOutput.ShouldBeTrue();
        ComplexityCommand.Info.Mutates.ShouldBeFalse();
    }

    // ── CLI input validation ────────────────────────────────────────────────

    [Theory]
    [InlineData("negative threshold", -1, "cyclomatic", 0)]
    [InlineData("negative top", 0, "cyclomatic", -1)]
    [InlineData("unknown sort key", 0, "banana", 0)]
    public async Task cli_returns_exit_code_1_for_invalid_arguments(string scenario, int threshold, string sort, int top)
    {
        _ = scenario;
        var exitCode = await _services.Resolve<ComplexityCommand>().ExecuteAsync(null, threshold, sort, top);
        exitCode.ShouldBe(1);
    }

    // -----------------------------------------------------------------------
    // Threshold: >= comparison (method at exactly threshold is included)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task threshold_method_at_exact_threshold_is_included()
    {
        var report = await AnalyzeSourceAsync("""
                                              public class C {
                                                  public void M(bool a, bool b) {
                                                      if (a) { }
                                                      if (b) { }
                                                  }
                                              }
                                              """);
        // M: cyclomatic = 3 (base=1 + if=1 + if=1)
        var unfiltered = report.Methods.First(m => m.Method.EndsWith(".M", StringComparison.Ordinal));
        var cyclomaticValue = unfiltered.Cyclomatic;

        var filtered = await AnalyzeSourceAsync("""
                                                public class C {
                                                    public void M(bool a, bool b) {
                                                        if (a) { }
                                                        if (b) { }
                                                    }
                                                }
                                                """, 3);
        // threshold == cyclomatic => method should be included (>= not >)
        filtered.Methods.ShouldContain(m => m.Method.EndsWith(".M", StringComparison.Ordinal));
    }

    // -----------------------------------------------------------------------
    // Rollups unaffected by --top
    // -----------------------------------------------------------------------

    [Fact]
    public async Task includes_rollups_for_all_methods_regardless_of_top()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Test.cs", """
                                     namespace MyNs;
                                     public class C {
                                         public void A() { var x = 1; }
                                         public void B() { var x = 1; }
                                         public void D() { var x = 1; }
                                     }
                                     """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln, top: 1);
            // Methods truncated to 1, but rollup covers all 3
            Assert.Single(report.Methods);
            var typeRollup = Assert.Single(report.TypeRollups);
            typeRollup.MethodCount.ShouldBe(3);
        }
    }

    // -----------------------------------------------------------------------
    // Async/await does not inflate cyclomatic
    // -----------------------------------------------------------------------

    [Fact]
    public async Task handles_async_await_does_not_inflate_cyclomatic()
    {
        var report = await AnalyzeSourceAsync("""
                                              public class C {
                                                  public async System.Threading.Tasks.Task<int> M() {
                                                      await System.Threading.Tasks.Task.Delay(0);
                                                      return 1;
                                                  }
                                              }
                                              """);
        // base=1 only — await adds no decision point
        GetMethod(report, "M").Cyclomatic.ShouldBe(1);
    }

    // -----------------------------------------------------------------------
    // Yield does not inflate cyclomatic
    // -----------------------------------------------------------------------

    [Fact]
    public async Task handles_yield_return_does_not_inflate_cyclomatic()
    {
        var report = await AnalyzeSourceAsync("""
                                              public class C {
                                                  public System.Collections.Generic.IEnumerable<int> M() {
                                                      yield return 1;
                                                      yield return 2;
                                                      yield break;
                                                  }
                                              }
                                              """);
        // base=1 only — yield return/break add no decision point
        GetMethod(report, "M").Cyclomatic.ShouldBe(1);
    }

    // -----------------------------------------------------------------------
    // Combined flags: threshold + sort + top each behave independently
    // -----------------------------------------------------------------------

    [Fact]
    public async Task handles_combined_threshold_sort_top_each_behave_independently()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Test.cs", """
                                     public class C {
                                         public void Low() { var x = 1; }
                                         public void Mid(bool a) { if (a) { } }
                                         public void High(bool a, bool b, bool c) {
                                             if (a) { } if (b) { } if (c) { }
                                         }
                                     }
                                     """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // threshold=2 filters out Low (cyclomatic=1); sort=cyclomatic desc; top=1 keeps only High
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln, 2, "cyclomatic", 1);
            Assert.Single(report.Methods);
            report.Methods.ShouldContain(m => m.Method.EndsWith(".High", StringComparison.Ordinal));
            // Rollups still cover all 3 methods
            var rollup = Assert.Single(report.TypeRollups);
            rollup.MethodCount.ShouldBe(3);
        }
    }
}
