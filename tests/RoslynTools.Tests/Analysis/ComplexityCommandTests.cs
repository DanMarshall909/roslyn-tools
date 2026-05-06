using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Analysis;

public class ComplexityCommandTests
{
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
            return await ComplexityAnalyzer.AnalyzeAsync(sln, threshold, sort, top);
    }

    // Matches by exact FQN or by the suffix after the last dot (e.g. "M" matches "C.M")
    private static MethodComplexity GetMethod(ComplexityReport report, string name) =>
        report.Methods.First(m => m.Method == name || m.Method.EndsWith("." + name, StringComparison.Ordinal));

    private static MethodComplexity? FindMethod(ComplexityReport report, string name) =>
        report.Methods.FirstOrDefault(m => m.Method == name || m.Method.EndsWith("." + name, StringComparison.Ordinal));

    // -----------------------------------------------------------------------
    // Cyclomatic: base = 1
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_simple_method_cyclomatic_is_1()
    {
        var report = await AnalyzeSourceAsync("""
            public class C {
                public void M() { var x = 1; }
            }
            """);
        Assert.Equal(1, GetMethod(report, "M").Cyclomatic);
    }

    // -----------------------------------------------------------------------
    // Cyclomatic: if (+1), but NOT bare else
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_if_else_cyclomatic_counts_only_if()
    {
        var report = await AnalyzeSourceAsync("""
            public class C {
                public void M(bool b) {
                    if (b) { var x = 1; }
                    else   { var y = 2; }
                }
            }
            """);
        // base=1 + if=1 = 2 (else does NOT add)
        Assert.Equal(2, GetMethod(report, "M").Cyclomatic);
    }

    // -----------------------------------------------------------------------
    // Cyclomatic: for, foreach, while, do (+1 each)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_loops_cyclomatic_increments_per_loop()
    {
        var report = await AnalyzeSourceAsync("""
            public class C {
                public void M(int[] arr) {
                    for (int i = 0; i < 10; i++) { }
                    foreach (var x in arr) { }
                    while (true) { break; }
                    do { break; } while (false);
                }
            }
            """);
        // base=1 + for=1 + foreach=1 + while=1 + do=1 = 5
        Assert.Equal(5, GetMethod(report, "M").Cyclomatic);
    }

    // -----------------------------------------------------------------------
    // Cyclomatic: catch (+1)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_catch_cyclomatic_increments()
    {
        var report = await AnalyzeSourceAsync("""
            public class C {
                public void M() {
                    try { }
                    catch (System.Exception) { }
                    catch (System.InvalidOperationException) { }
                }
            }
            """);
        // base=1 + catch=1 + catch=1 = 3
        Assert.Equal(3, GetMethod(report, "M").Cyclomatic);
    }

    // -----------------------------------------------------------------------
    // Cyclomatic: ternary ?:, ?? , &&, ||, ?.  (+1 each)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_conditional_operators_cyclomatic_increments()
    {
        var report = await AnalyzeSourceAsync("""
            public class C {
                public string? M(bool b, string? s, string? t) {
                    var x = b ? "a" : "b";
                    var y = s ?? t;
                    var z = s?.Length;
                    return x;
                }
            }
            """);
        // base=1 + ternary=1 + ??=1 + ?.=1 = 4
        Assert.Equal(4, GetMethod(report, "M").Cyclomatic);
    }

    [Fact]
    public async Task with_logical_and_or_cyclomatic_increments()
    {
        var report = await AnalyzeSourceAsync("""
            public class C {
                public bool M(bool a, bool b, bool c) {
                    return a && b || c;
                }
            }
            """);
        // base=1 + &&=1 + ||=1 = 3
        Assert.Equal(3, GetMethod(report, "M").Cyclomatic);
    }

    [Fact]
    public async Task with_coalescence_assignment_cyclomatic_increments()
    {
        var report = await AnalyzeSourceAsync("""
            public class C {
                public void M(string? s) {
                    s ??= "default";
                }
            }
            """);
        // base=1 + ??==1 = 2
        Assert.Equal(2, GetMethod(report, "M").Cyclomatic);
    }

    // -----------------------------------------------------------------------
    // Cyclomatic: switch statement — non-default labels only
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_switch_statement_cyclomatic_counts_non_default_labels()
    {
        var report = await AnalyzeSourceAsync("""
            public class C {
                public void M(int x) {
                    switch (x) {
                        case 1: break;
                        case 2: break;
                        default: break;
                    }
                }
            }
            """);
        // base=1 + case1=1 + case2=1 (default excluded) = 3
        Assert.Equal(3, GetMethod(report, "M").Cyclomatic);
    }

    // -----------------------------------------------------------------------
    // Cyclomatic: switch expression — non-discard arms only
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_switch_expression_cyclomatic_counts_non_discard_arms()
    {
        var report = await AnalyzeSourceAsync("""
            public class C {
                public string M(int x) => x switch {
                    1 => "one",
                    2 => "two",
                    _ => "other"
                };
            }
            """);
        // base=1 + arm1=1 + arm2=1 (discard _ excluded) = 3
        Assert.Equal(3, GetMethod(report, "M").Cyclomatic);
    }

    // -----------------------------------------------------------------------
    // LOC: expression-bodied method = 1
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_expression_bodied_method_loc_is_1()
    {
        var report = await AnalyzeSourceAsync("""
            public class C {
                public int M() => 42;
            }
            """);
        Assert.Equal(1, GetMethod(report, "M").Loc);
    }

    // -----------------------------------------------------------------------
    // LOC: blank lines and braces excluded
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_block_body_loc_excludes_blank_lines_and_braces()
    {
        var report = await AnalyzeSourceAsync("""
            public class C {
                public void M() {

                    var x = 1;

                    var y = 2;

                }
            }
            """);
        // Only "var x = 1;" and "var y = 2;" — blank lines and braces excluded
        Assert.Equal(2, GetMethod(report, "M").Loc);
    }

    // -----------------------------------------------------------------------
    // LOC: comment-only lines excluded
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_comment_lines_loc_excludes_comments()
    {
        var report = await AnalyzeSourceAsync("""
            public class C {
                public void M() {
                    // this is a comment
                    var x = 1;
                    /* block comment */
                    var y = 2;
                }
            }
            """);
        // var x=1, var y=2 = 2 (comments excluded)
        Assert.Equal(2, GetMethod(report, "M").Loc);
    }

    // -----------------------------------------------------------------------
    // Params: basic count
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_regular_method_params_counts_all_parameters()
    {
        var report = await AnalyzeSourceAsync("""
            public class C {
                public void M(int a, string b, bool c) { }
            }
            """);
        Assert.Equal(3, GetMethod(report, "M").Params);
    }

    // -----------------------------------------------------------------------
    // Params: extension method subtracts 'this'
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_extension_method_params_excludes_this_parameter()
    {
        var report = await AnalyzeSourceAsync("""
            public static class C {
                public static void M(this string s, int n) { }
            }
            """);
        // 2 parameters but 'this' subtracted = 1
        Assert.Equal(1, GetMethod(report, "M").Params);
    }

    // -----------------------------------------------------------------------
    // Member coverage: constructors
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_constructor_it_is_included()
    {
        var report = await AnalyzeSourceAsync("""
            public class C {
                public C(int x) { var y = x + 1; }
            }
            """);
        Assert.Contains(report.Methods, m => m.Method.EndsWith(".C", StringComparison.Ordinal));
    }

    // -----------------------------------------------------------------------
    // Member coverage: accessor with body
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_property_getter_body_it_is_included()
    {
        var report = await AnalyzeSourceAsync("""
            public class C {
                private int _x;
                public int X {
                    get { return _x; }
                    set { _x = value; }
                }
            }
            """);
        Assert.Contains(report.Methods, m => m.Method.EndsWith(".X.get", StringComparison.Ordinal));
        Assert.Contains(report.Methods, m => m.Method.EndsWith(".X.set", StringComparison.Ordinal));
    }

    // -----------------------------------------------------------------------
    // Member coverage: auto-property accessors skipped
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_auto_property_accessors_are_skipped()
    {
        var report = await AnalyzeSourceAsync("""
            public class C {
                public int X { get; set; }
            }
            """);
        // No body — auto-property accessors should NOT appear
        Assert.DoesNotContain(report.Methods, m => m.Method.EndsWith(".X.get", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Methods, m => m.Method.EndsWith(".X.set", StringComparison.Ordinal));
    }

    // -----------------------------------------------------------------------
    // Member coverage: abstract method skipped
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_abstract_method_it_is_skipped()
    {
        var report = await AnalyzeSourceAsync("""
            public abstract class C {
                public abstract void M();
                public void N() { var x = 1; }
            }
            """);
        Assert.DoesNotContain(report.Methods, m => m.Method.EndsWith(".M", StringComparison.Ordinal));
        Assert.Contains(report.Methods, m => m.Method.EndsWith(".N", StringComparison.Ordinal));
    }

    // -----------------------------------------------------------------------
    // Member coverage: partial method without body skipped
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_partial_method_without_body_it_is_skipped()
    {
        var report = await AnalyzeSourceAsync("""
            public partial class C {
                partial void M();
                public void N() { var x = 1; }
            }
            """);
        Assert.DoesNotContain(report.Methods, m => m.Method.EndsWith(".M", StringComparison.Ordinal));
        Assert.Contains(report.Methods, m => m.Method.EndsWith(".N", StringComparison.Ordinal));
    }

    // -----------------------------------------------------------------------
    // Member coverage: local function included
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_local_function_it_is_included()
    {
        var report = await AnalyzeSourceAsync("""
            public class C {
                public void M() {
                    int Local(int x) { return x + 1; }
                    Local(1);
                }
            }
            """);
        Assert.Contains(report.Methods, m => m.Method.EndsWith(".Local", StringComparison.Ordinal));
    }

    // -----------------------------------------------------------------------
    // Skip: generated files (.g.cs)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_generated_file_g_cs_methods_are_skipped()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Generated.g.cs", """
            public class Generated {
                public void M() { var x = 1; }
            }
            """);
        project.AddSource("Real.cs", """
            public class Real {
                public void N() { var x = 1; }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.DoesNotContain(report.Methods, m => m.Type == "Generated");
            Assert.Contains(report.Methods, m => m.Type == "Real");
        }
    }

    // -----------------------------------------------------------------------
    // Skip: generated files (.Designer.cs)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_designer_file_methods_are_skipped()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Form1.Designer.cs", """
            public class Form1 {
                public void InitializeComponent() { var x = 1; }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.DoesNotContain(report.Methods, m => m.Type == "Form1");
        }
    }

    // -----------------------------------------------------------------------
    // Skip: auto-generated marker in file content
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_auto_generated_comment_methods_are_skipped()
    {
        var report = await AnalyzeSourceAsync("""
            // <auto-generated>
            public class Gen {
                public void M() { var x = 1; }
            }
            """);
        Assert.DoesNotContain(report.Methods, m => m.Type == "Gen");
    }

    // -----------------------------------------------------------------------
    // Threshold filter: only methods with ANY metric >= threshold included
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_threshold_only_methods_meeting_threshold_are_returned()
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
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln, threshold: 4);
            // Complex has cyclomatic >= 4; Simple does not
            Assert.DoesNotContain(report.Methods, m => m.Method.EndsWith(".Simple", StringComparison.Ordinal));
            Assert.Contains(report.Methods, m => m.Method.EndsWith(".Complex", StringComparison.Ordinal));
        }
    }

    // -----------------------------------------------------------------------
    // Rollups: computed over ALL methods regardless of filter
    // -----------------------------------------------------------------------

    [Fact]
    public async Task rollups_include_all_methods_regardless_of_threshold_filter()
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
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln, threshold: 10);
            Assert.Empty(report.Methods);
            var typeRollup = Assert.Single(report.TypeRollups);
            Assert.Equal("MyNs.C", typeRollup.Type);
            Assert.Equal(2, typeRollup.MethodCount);
        }
    }

    // -----------------------------------------------------------------------
    // Sort: cyclomatic descending
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_sort_cyclomatic_methods_ordered_descending_by_cyclomatic()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Test.cs", """
            public class C {
                public void Low() { var x = 1; }
                public void High(bool a, bool b, bool c) {
                    if (a) { } if (b) { } if (c) { }
                }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln, sort: "cyclomatic");
            var methods = report.Methods.ToList();
            // High should come before Low
            var highIdx = methods.FindIndex(m => m.Method.EndsWith(".High", StringComparison.Ordinal));
            var lowIdx = methods.FindIndex(m => m.Method.EndsWith(".Low", StringComparison.Ordinal));
            Assert.True(highIdx < lowIdx, "High complexity method should sort before Low");
        }
    }

    // -----------------------------------------------------------------------
    // Sort: cognitive descending
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_sort_cognitive_methods_ordered_descending_by_cognitive()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Test.cs", """
            public class C {
                public void Low() { var x = 1; }
                public void High(bool a, bool b) {
                    if (a) {
                        if (b) { }
                    }
                }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln, sort: "cognitive");
            var methods = report.Methods.ToList();
            var highIdx = methods.FindIndex(m => m.Method.EndsWith(".High", StringComparison.Ordinal));
            var lowIdx = methods.FindIndex(m => m.Method.EndsWith(".Low", StringComparison.Ordinal));
            Assert.True(highIdx < lowIdx, "High cognitive method should sort before Low");
        }
    }

    // -----------------------------------------------------------------------
    // Sort: loc descending
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_sort_loc_methods_ordered_descending_by_loc()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Test.cs", """
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
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln, sort: "loc");
            var methods = report.Methods.ToList();
            var longIdx = methods.FindIndex(m => m.Method.EndsWith(".Long", StringComparison.Ordinal));
            var shortIdx = methods.FindIndex(m => m.Method.EndsWith(".Short", StringComparison.Ordinal));
            Assert.True(longIdx < shortIdx, "Long method should sort before Short");
        }
    }

    // -----------------------------------------------------------------------
    // Sort: params descending
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_sort_params_methods_ordered_descending_by_params()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Test.cs", """
            public class C {
                public void Few(int a) { }
                public void Many(int a, int b, int c, int d, int e) { }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln, sort: "params");
            var methods = report.Methods.ToList();
            var manyIdx = methods.FindIndex(m => m.Method.EndsWith(".Many", StringComparison.Ordinal));
            var fewIdx = methods.FindIndex(m => m.Method.EndsWith(".Few", StringComparison.Ordinal));
            Assert.True(manyIdx < fewIdx, "Many-params method should sort before Few-params");
        }
    }

    // -----------------------------------------------------------------------
    // Top: truncates results
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_top_results_are_truncated_to_n()
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
            Assert.Equal(2, report.Methods.Count);
        }
    }

    // -----------------------------------------------------------------------
    // Rollups: type rollup aggregates correctly
    // -----------------------------------------------------------------------

    [Fact]
    public async Task type_rollup_aggregates_method_metrics()
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
            Assert.Equal("MyApp.MyClass", rollup.Type);
            Assert.Equal("MyApp", rollup.Namespace);
            Assert.Equal(2, rollup.MethodCount);
            // A: cyclomatic=1, B: cyclomatic=2 => total=3
            Assert.Equal(3, rollup.CyclomaticTotal);
        }
    }

    // -----------------------------------------------------------------------
    // Rollups: namespace rollup aggregates correctly
    // -----------------------------------------------------------------------

    [Fact]
    public async Task namespace_rollup_aggregates_across_types()
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
            Assert.Equal("MyNs", nsRollup.Namespace);
            Assert.Equal(2, nsRollup.MethodCount);
        }
    }

    // -----------------------------------------------------------------------
    // Cognitive: nested structures increase cost
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_nested_ifs_cognitive_reflects_nesting_penalty()
    {
        // Outer if: 1 + depth(0) = 1
        // Inner if: 1 + depth(1) = 2
        // Total = 3
        var report = await AnalyzeSourceAsync("""
            public class C {
                public void M(bool a, bool b) {
                    if (a) {
                        if (b) { }
                    }
                }
            }
            """);
        Assert.Equal(3, GetMethod(report, "M").Cognitive);
    }

    // -----------------------------------------------------------------------
    // Cognitive: else adds +1 without depth
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_if_else_cognitive_else_adds_1_no_depth()
    {
        // if: 1+0=1, else: +1 = total 2
        var report = await AnalyzeSourceAsync("""
            public class C {
                public void M(bool b) {
                    if (b) { var x = 1; }
                    else { var y = 2; }
                }
            }
            """);
        Assert.Equal(2, GetMethod(report, "M").Cognitive);
    }

    // -----------------------------------------------------------------------
    // Metadata: file path and line populated
    // -----------------------------------------------------------------------

    [Fact]
    public async Task method_has_file_path_and_line_populated()
    {
        var report = await AnalyzeSourceAsync("""
            public class C {
                public void M() { var x = 1; }
            }
            """);
        var m = GetMethod(report, "M");
        Assert.False(string.IsNullOrEmpty(m.File));
        Assert.True(m.Line > 0);
    }

    // -----------------------------------------------------------------------
    // Metadata: type and namespace populated
    // -----------------------------------------------------------------------

    [Fact]
    public async Task method_has_type_and_namespace_populated()
    {
        var report = await AnalyzeSourceAsync("""
            namespace Foo.Bar;
            public class MyType {
                public void M() { var x = 1; }
            }
            """);
        var m = GetMethod(report, "M");
        Assert.Equal("Foo.Bar.MyType", m.Type);
        Assert.Equal("Foo.Bar", m.Namespace);
    }

    // -----------------------------------------------------------------------
    // Empty project returns empty report
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_no_methods_report_is_empty()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Empty.cs", "// nothing here");
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.Empty(report.Methods);
            Assert.Empty(report.TypeRollups);
            Assert.Empty(report.NamespaceRollups);
        }
    }

    // -----------------------------------------------------------------------
    // IRtCommand contract
    // -----------------------------------------------------------------------

    [Fact]
    public void Info_category_is_analyze() =>
        Assert.Equal("analyze", ComplexityCommand.Info.Category);

    [Fact]
    public void Info_json_output_is_true() =>
        Assert.True(ComplexityCommand.Info.JsonOutput);

    [Fact]
    public void Info_mutates_is_false() =>
        Assert.False(ComplexityCommand.Info.Mutates);

    // -----------------------------------------------------------------------
    // CLI validation: invalid inputs return exit code 1
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_negative_threshold_returns_exit_code_1()
    {
        var exitCode = await ComplexityCommand.ExecuteAsync(null, threshold: -1, sort: "cyclomatic", top: 0);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task with_negative_top_returns_exit_code_1()
    {
        var exitCode = await ComplexityCommand.ExecuteAsync(null, threshold: 0, sort: "cyclomatic", top: -1);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task with_invalid_sort_returns_exit_code_1()
    {
        var exitCode = await ComplexityCommand.ExecuteAsync(null, threshold: 0, sort: "banana", top: 0);
        Assert.Equal(1, exitCode);
    }

    // -----------------------------------------------------------------------
    // Threshold: >= comparison (method at exactly threshold is included)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task with_threshold_method_at_exact_threshold_is_included()
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
            """, threshold: 3);
        // threshold == cyclomatic => method should be included (>= not >)
        Assert.Contains(filtered.Methods, m => m.Method.EndsWith(".M", StringComparison.Ordinal));
    }

    // -----------------------------------------------------------------------
    // Rollups unaffected by --top
    // -----------------------------------------------------------------------

    [Fact]
    public async Task rollups_include_all_methods_regardless_of_top()
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
            Assert.Equal(3, typeRollup.MethodCount);
        }
    }

    // -----------------------------------------------------------------------
    // Async/await does not inflate cyclomatic
    // -----------------------------------------------------------------------

    [Fact]
    public async Task async_await_does_not_inflate_cyclomatic()
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
        Assert.Equal(1, GetMethod(report, "M").Cyclomatic);
    }

    // -----------------------------------------------------------------------
    // Yield does not inflate cyclomatic
    // -----------------------------------------------------------------------

    [Fact]
    public async Task yield_return_does_not_inflate_cyclomatic()
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
        Assert.Equal(1, GetMethod(report, "M").Cyclomatic);
    }

    // -----------------------------------------------------------------------
    // Combined flags: threshold + sort + top each behave independently
    // -----------------------------------------------------------------------

    [Fact]
    public async Task combined_threshold_sort_top_each_behave_independently()
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
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln, threshold: 2, sort: "cyclomatic", top: 1);
            Assert.Single(report.Methods);
            Assert.Contains(report.Methods, m => m.Method.EndsWith(".High", StringComparison.Ordinal));
            // Rollups still cover all 3 methods
            var rollup = Assert.Single(report.TypeRollups);
            Assert.Equal(3, rollup.MethodCount);
        }
    }
}
