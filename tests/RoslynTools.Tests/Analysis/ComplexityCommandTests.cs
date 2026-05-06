using RoslynTools.Services;

namespace RoslynTools.Tests.Analysis;

/// <summary>
/// Acceptance tests for rt analyze complexity.
/// Red phase — ComplexityAnalyzer and ComplexityReport do not exist yet.
/// </summary>
public class ComplexityCommandTests
{
    // ── IRtCommand contract ──────────────────────────────────────────────────

    [Fact]
    public void Info_category_is_analyze()
        => throw new NotImplementedException();

    [Fact]
    public void Info_json_output_is_true()
        => throw new NotImplementedException();

    [Fact]
    public void Info_mutates_is_false()
        => throw new NotImplementedException();

    [Fact]
    public void Command_is_registered_and_reachable_as_root_shortcut()
        => throw new NotImplementedException();

    // ── Output shape ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Output_json_contains_methods_typeRollups_namespaceRollups()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", "public class A { public void M() {} }");
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.NotNull(report.Methods);
            Assert.NotNull(report.TypeRollups);
            Assert.NotNull(report.NamespaceRollups);
        }
    }

    [Fact]
    public async Task Method_entry_contains_method_type_namespace_file_line_cyclomatic_cognitive_loc_params()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", "public class A { public void M() {} }");
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            var m = Assert.Single(report.Methods);
            Assert.NotEmpty(m.Method);
            Assert.NotEmpty(m.Type);
            Assert.NotNull(m.Namespace);
            Assert.NotEmpty(m.File);
            Assert.True(m.Line >= 1);
            Assert.True(m.Cyclomatic >= 1);
            Assert.True(m.Cognitive >= 0);
            Assert.True(m.Loc >= 0);
            Assert.True(m.Params >= 0);
        }
    }

    // ── Cyclomatic complexity ────────────────────────────────────────────────

    [Fact]
    public async Task with_empty_method_cyclomatic_is_1()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", "public class A { public void M() {} }");
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.Equal(1, report.Methods.Single(m => m.Method.EndsWith(".M")).Cyclomatic);
        }
    }

    [Fact]
    public async Task with_single_if_no_else_cyclomatic_is_2()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A {
                public void M(bool x) { if (x) { } }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.Equal(2, report.Methods.Single(m => m.Method.EndsWith(".M")).Cyclomatic);
        }
    }

    [Fact]
    public async Task with_if_elseif_else_bare_else_does_not_increment_cyclomatic()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A {
                public void M(int x) {
                    if (x == 1) { }
                    else if (x == 2) { }
                    else { }
                }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            // if + else if = 2 decisions; bare else = 0 → base 1 + 2 = 3
            Assert.Equal(3, report.Methods.Single(m => m.Method.EndsWith(".M")).Cyclomatic);
        }
    }

    [Fact]
    public async Task with_switch_n_case_labels_cyclomatic_increments_by_n()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A {
                public void M(int x) {
                    switch (x) { case 1: break; case 2: break; case 3: break; default: break; }
                }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            // 3 non-default cases → base 1 + 3 = 4
            Assert.Equal(4, report.Methods.Single(m => m.Method.EndsWith(".M")).Cyclomatic);
        }
    }

    [Fact]
    public async Task with_for_loop_cyclomatic_increments()
        => throw new NotImplementedException();

    [Fact]
    public async Task with_foreach_loop_cyclomatic_increments()
        => throw new NotImplementedException();

    [Fact]
    public async Task with_while_loop_cyclomatic_increments()
        => throw new NotImplementedException();

    [Fact]
    public async Task with_do_while_loop_cyclomatic_increments()
        => throw new NotImplementedException();

    [Fact]
    public async Task with_catch_clause_cyclomatic_increments()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A {
                public void M() {
                    try { } catch (System.Exception) { }
                }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.Equal(2, report.Methods.Single(m => m.Method.EndsWith(".M")).Cyclomatic);
        }
    }

    [Fact]
    public async Task with_ternary_cyclomatic_increments()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A {
                public int M(bool x) => x ? 1 : 0;
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.Equal(2, report.Methods.Single(m => m.Method.EndsWith(".M")).Cyclomatic);
        }
    }

    [Fact]
    public async Task with_null_coalescing_cyclomatic_increments()
        => throw new NotImplementedException();

    [Fact]
    public async Task with_null_conditional_cyclomatic_increments()
        => throw new NotImplementedException();

    [Fact]
    public async Task with_logical_and_cyclomatic_increments()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A {
                public bool M(bool a, bool b) => a && b;
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.Equal(2, report.Methods.Single(m => m.Method.EndsWith(".M")).Cyclomatic);
        }
    }

    [Fact]
    public async Task with_logical_or_cyclomatic_increments()
        => throw new NotImplementedException();

    [Fact]
    public async Task with_null_coalescing_assignment_cyclomatic_increments()
        => throw new NotImplementedException();

    [Fact]
    public async Task with_pattern_switch_arm_default_discard_does_not_increment_cyclomatic()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A {
                public string M(int x) => x switch { 1 => "one", 2 => "two", _ => "other" };
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            // 2 non-default arms → base 1 + 2 = 3
            Assert.Equal(3, report.Methods.Single(m => m.Method.EndsWith(".M")).Cyclomatic);
        }
    }

    // ── Cognitive complexity ─────────────────────────────────────────────────

    [Fact]
    public async Task with_empty_method_cognitive_is_0()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", "public class A { public void M() {} }");
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.Equal(0, report.Methods.Single(m => m.Method.EndsWith(".M")).Cognitive);
        }
    }

    [Fact]
    public async Task with_if_at_depth_0_cognitive_adds_1()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A { public void M(bool x) { if (x) { } } }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.Equal(1, report.Methods.Single(m => m.Method.EndsWith(".M")).Cognitive);
        }
    }

    [Fact]
    public async Task with_nested_if_cognitive_applies_nesting_multiplier()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A {
                public void M(bool a, bool b) {
                    if (a) {         // depth 0 → +1
                        if (b) { }   // depth 1 → +2
                    }
                }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.Equal(3, report.Methods.Single(m => m.Method.EndsWith(".M")).Cognitive);
        }
    }

    [Fact]
    public async Task with_else_clause_cognitive_adds_1()
        => throw new NotImplementedException();

    [Fact]
    public async Task with_logical_operator_type_transition_cognitive_adds_1_per_transition()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A {
                public bool M(bool a, bool b, bool c) => a && b || c;
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            // one && sequence (+1) + one || sequence (+1) = 2
            Assert.Equal(2, report.Methods.Single(m => m.Method.EndsWith(".M")).Cognitive);
        }
    }

    [Fact]
    public async Task with_recursive_call_cognitive_adds_1()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A {
                public int M(int n) => n <= 0 ? 0 : M(n - 1);
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            var m = report.Methods.Single(x => x.Method.EndsWith(".M"));
            // ternary +1 (cyclomatic), recursive call +1 (cognitive)
            Assert.True(m.Cognitive >= 2);
        }
    }

    // ── LOC ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task with_empty_method_loc_is_0()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", "public class A { public void M() {} }");
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.Equal(0, report.Methods.Single(m => m.Method.EndsWith(".M")).Loc);
        }
    }

    [Fact]
    public async Task with_single_return_loc_is_1()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A {
                public int M() {
                    return 1;
                }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.Equal(1, report.Methods.Single(m => m.Method.EndsWith(".M")).Loc);
        }
    }

    [Fact]
    public async Task loc_excludes_single_line_comments()
        => throw new NotImplementedException();

    [Fact]
    public async Task loc_excludes_block_comment_only_lines()
        => throw new NotImplementedException();

    [Fact]
    public async Task with_expression_bodied_method_loc_is_1()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", "public class A { public int M() => 1; }");
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.Equal(1, report.Methods.Single(m => m.Method.EndsWith(".M")).Loc);
        }
    }

    // ── Params ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task with_no_params_count_is_0()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", "public class A { public void M() {} }");
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.Equal(0, report.Methods.Single(m => m.Method.EndsWith(".M")).Params);
        }
    }

    [Fact]
    public async Task extension_method_this_param_excluded_from_count()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public static class A {
                public static int M(this string s, int x) => x;
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.Equal(1, report.Methods.Single(m => m.Method.EndsWith(".M")).Params);
        }
    }

    // ── Member type coverage ─────────────────────────────────────────────────

    [Fact]
    public async Task constructors_are_included_in_methods()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A {
                public A(int x) { }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.Contains(report.Methods, m => m.Method.Contains(".ctor") || m.Method.Contains(".A"));
        }
    }

    [Fact]
    public async Task property_accessors_are_reported_as_separate_entries()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A {
                private int _x;
                public int X { get => _x; set => _x = value; }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.Contains(report.Methods, m => m.Method.Contains(".X.get") || m.Method.Contains(".get_X"));
            Assert.Contains(report.Methods, m => m.Method.Contains(".X.set") || m.Method.Contains(".set_X"));
        }
    }

    [Fact]
    public async Task local_functions_are_reported_as_separate_entries()
        => throw new NotImplementedException();

    [Fact]
    public async Task lambdas_are_not_reported_as_separate_entries()
        => throw new NotImplementedException();

    [Fact]
    public async Task abstract_methods_produce_no_entry()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public abstract class A {
                public abstract void M();
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.DoesNotContain(report.Methods, m => m.Method.EndsWith(".M"));
        }
    }

    [Fact]
    public async Task auto_implemented_properties_produce_no_entry()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", "public class A { public int X { get; set; } }");
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.Empty(report.Methods);
        }
    }

    [Fact]
    public async Task generated_files_with_auto_generated_header_are_skipped()
        => throw new NotImplementedException();

    [Fact]
    public async Task files_with_g_cs_suffix_are_skipped()
        => throw new NotImplementedException();

    // ── Rollups ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task type_rollups_sum_per_method_metrics()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A {
                public void M1() { }
                public void M2(int x) { }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            var rollup = report.TypeRollups.Single(r => r.Type.EndsWith(".A") || r.Type == "A");
            Assert.Equal(2, rollup.MethodCount);
            Assert.Equal(report.Methods.Sum(m => m.Cyclomatic), rollup.CyclomaticTotal);
        }
    }

    [Fact]
    public async Task namespace_rollups_sum_type_metrics()
        => throw new NotImplementedException();

    [Fact]
    public async Task rollups_not_affected_by_threshold_filter()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A {
                public void Simple() { }
                public void Complex(bool a, bool b, bool c, bool d, bool e, bool f, bool g, bool h) {
                    if (a) { if (b) { if (c) { if (d) { if (e) { if (f) {} } } } } }
                    if (g && h) { }
                }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var all = await ComplexityAnalyzer.AnalyzeAsync(sln, threshold: 0);
            var filtered = await ComplexityAnalyzer.AnalyzeAsync(sln, threshold: 10);

            Assert.True(filtered.Methods.Count < all.Methods.Count);
            Assert.Equal(
                all.TypeRollups.Single().CyclomaticTotal,
                filtered.TypeRollups.Single().CyclomaticTotal);
        }
    }

    [Fact]
    public async Task rollups_not_affected_by_top_truncation()
        => throw new NotImplementedException();

    // ── Threshold ────────────────────────────────────────────────────────────

    [Fact]
    public async Task with_threshold_0_all_methods_included()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A {
                public void M1() { }
                public void M2() { }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln, threshold: 0);
            Assert.Equal(2, report.Methods.Count);
        }
    }

    [Fact]
    public async Task with_threshold_method_at_exactly_threshold_is_included()
        => throw new NotImplementedException();

    [Fact]
    public async Task with_threshold_method_below_on_all_metrics_is_excluded()
        => throw new NotImplementedException();

    [Fact]
    public async Task with_threshold_method_above_on_one_metric_is_included()
        => throw new NotImplementedException();

    [Fact]
    public async Task with_negative_threshold_throws_or_exits_1()
        => throw new NotImplementedException();

    // ── Sort ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task sort_cyclomatic_descending_is_default()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A {
                public void Low() { }
                public void High(bool a, bool b, bool c) { if (a) { if (b) { if (c) {} } } }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln);
            Assert.True(report.Methods[0].Cyclomatic >= report.Methods[^1].Cyclomatic);
        }
    }

    [Fact]
    public async Task sort_cognitive_orders_descending()
        => throw new NotImplementedException();

    [Fact]
    public async Task sort_loc_orders_descending()
        => throw new NotImplementedException();

    [Fact]
    public async Task sort_params_orders_descending()
        => throw new NotImplementedException();

    [Fact]
    public async Task sort_invalid_value_throws_or_exits_1()
        => throw new NotImplementedException();

    // ── Top ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task top_0_does_not_truncate()
        => throw new NotImplementedException();

    [Fact]
    public async Task top_n_truncates_after_sort()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            public class A {
                public void M1() { }
                public void M2() { }
                public void M3() { }
            }
            """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var report = await ComplexityAnalyzer.AnalyzeAsync(sln, top: 2);
            Assert.Equal(2, report.Methods.Count);
        }
    }

    [Fact]
    public async Task top_exceeding_count_returns_all()
        => throw new NotImplementedException();

    [Fact]
    public async Task top_negative_throws_or_exits_1()
        => throw new NotImplementedException();

    // ── Combination ──────────────────────────────────────────────────────────

    [Fact]
    public async Task threshold_sort_top_combine_independently()
        => throw new NotImplementedException();

    // ── Async / iterator edge cases ──────────────────────────────────────────

    [Fact]
    public async Task async_await_does_not_add_to_cyclomatic_or_cognitive()
        => throw new NotImplementedException();

    [Fact]
    public async Task yield_return_does_not_add_to_cyclomatic_or_cognitive()
        => throw new NotImplementedException();

    // ── Dogfood ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task runs_against_roslyntools_solution_and_finds_methods()
    {
        var solutionPath = Path.Combine(
            Path.GetDirectoryName(typeof(ComplexityCommandTests).Assembly.Location)!,
            "..", "..", "..", "..", "..", "RoslynTools.slnx");

        using var workspace = Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create();
        var sln = await workspace.OpenSolutionAsync(Path.GetFullPath(solutionPath));

        var report = await ComplexityAnalyzer.AnalyzeAsync(sln, threshold: 0);
        Assert.NotEmpty(report.Methods);
    }

    [Fact]
    public async Task empty_solution_produces_empty_arrays()
        => throw new NotImplementedException();
}
