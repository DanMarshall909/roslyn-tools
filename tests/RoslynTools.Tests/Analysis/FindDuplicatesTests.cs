using RoslynTools.Core;

namespace RoslynTools.Tests.Analysis;

public class FindDuplicatesTests
{
    // ── Exact duplicates within a single file detected ────────────────────────────

    [Fact]
    public async Task Detects_exact_duplicates_inside_one_file()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void First()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }

                public void Second()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions());

            Assert.NotEmpty(groups);
            var g = groups.First();
            Assert.Equal(2, g.Occurrences.Count);
            Assert.Equal(3, g.StatementCount);
        }
    }

    // ── Exact duplicates across files detected ────────────────────────────────────

    [Fact]
    public async Task Detects_exact_duplicates_across_files()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);
        project.AddSource("B.cs", """
            namespace TestApp;

            public class B
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions());

            Assert.NotEmpty(groups);
            var g = groups.First(g => g.StatementCount == 3);
            Assert.Equal(2, g.Occurrences.Count);
            var files = g.Occurrences.Select(o => Path.GetFileName(o.FilePath)).ToHashSet();
            Assert.Contains("A.cs", files);
            Assert.Contains("B.cs", files);
        }
    }

    // ── No duplicates returns empty list ──────────────────────────────────────────

    [Fact]
    public async Task Returns_empty_when_no_duplicates()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions());

            Assert.Empty(groups);
        }
    }

    // ── min-statements filter ─────────────────────────────────────────────────────

    [Fact]
    public async Task MinStatements_filters_shorter_duplicates()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void First()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }

                public void Second()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions(MinStatements: 5));

            Assert.Empty(groups);
        }
    }

    // ── min-occurrences filter ────────────────────────────────────────────────────

    [Fact]
    public async Task MinOccurrences_filters_groups_with_too_few_occurrences()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void First()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }

                public void Second()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions(MinOccurrences: 3));

            Assert.Empty(groups);
        }
    }

    // ── Bottom-up: only maximal clone reported, not sub-sequences ────────────────

    [Fact]
    public async Task Reports_only_maximal_clone_not_sub_sequences()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void First()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                    int z = 3;
                    System.Console.WriteLine(z);
                }

                public void Second()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                    int z = 3;
                    System.Console.WriteLine(z);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions(MinStatements: 3));

            // The 5-statement clone is the maximal block; sub-sequences (3-stmt, 4-stmt windows)
            // should not produce separate reported groups since all their occurrences are subsumed.
            Assert.NotEmpty(groups);
            var maximalGroup = groups.OrderByDescending(g => g.StatementCount).First();
            Assert.Equal(5, maximalGroup.StatementCount);
            Assert.Equal(2, maximalGroup.Occurrences.Count);
        }
    }

    // ── Bottom-up: PartiallyExpandable flag when some occurrences extend further ─

    [Fact]
    public async Task Flags_partial_expansion_when_some_occurrences_extend()
    {
        // Three methods share [s1,s2,s3]; two of them also share s4 (same invocation target),
        // but the third has a structurally different 4th statement (Write vs WriteLine).
        // Since BlockNormalizer preserves member names, Write and WriteLine are distinct —
        // only MethodA and MethodC share all 4 statements.
        // Expect: group [s1,s2,s3] with 3 occurrences and PartiallyExpandable=true
        //         group [s1,s2,s3,s4] with 2 occurrences and PartiallyExpandable=false
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void MethodA()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                    System.Console.WriteLine(x);
                }

                public void MethodB()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                    System.Console.Write(x);
                }

                public void MethodC()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                    System.Console.WriteLine(x);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions(MinStatements: 3));

            // The [s1,s2,s3] group appears in all 3 methods with PartiallyExpandable=true.
            // Use 3-occurrence count + PartiallyExpandable as the discriminator since there may be
            // other 3-statement groups (e.g. [s2,s3,s4] from MethodA and MethodC).
            var threeStmtGroup = groups.FirstOrDefault(g => g.StatementCount == 3 && g.Occurrences.Count == 3);
            Assert.NotNull(threeStmtGroup);
            Assert.True(threeStmtGroup.PartiallyExpandable,
                "3-statement group should be flagged PartiallyExpandable because some occurrences have an identical 4th statement");

            var fourStmtGroup = groups.FirstOrDefault(g => g.StatementCount == 4);
            Assert.NotNull(fourStmtGroup);
            Assert.Equal(2, fourStmtGroup.Occurrences.Count);
            Assert.False(fourStmtGroup.PartiallyExpandable,
                "4-statement group should not be PartiallyExpandable because no occurrences extend further");
        }
    }

    // ── Boilerplate-only blocks not reported ──────────────────────────────────────

    [Fact]
    public async Task Does_not_report_boilerplate_only_blocks()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void First()
                {
                    throw new System.NotImplementedException();
                }

                public void Second()
                {
                    throw new System.NotImplementedException();
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions(MinStatements: 1));

            Assert.Empty(groups);
        }
    }

    // ── US-202: Renamed local variable still groups ───────────────────────────────

    [Fact]
    public async Task Renamed_local_variable_still_groups()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void First()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }

                public void Second()
                {
                    int alpha = 1;
                    int beta = 2;
                    System.Console.WriteLine(alpha + beta);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions());

            Assert.NotEmpty(groups);
            var g = groups.First(g => g.StatementCount == 3);
            Assert.Equal(2, g.Occurrences.Count);
        }
    }

    // ── US-202: Renamed parameter still groups ────────────────────────────────────

    [Fact]
    public async Task Renamed_parameter_still_groups()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void First(int value, string label)
                {
                    int x = value;
                    int y = value + 1;
                    System.Console.WriteLine(label);
                    System.Console.WriteLine(x + y);
                }

                public void Second(int amount, string tag)
                {
                    int x = amount;
                    int y = amount + 1;
                    System.Console.WriteLine(tag);
                    System.Console.WriteLine(x + y);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions(MinStatements: 3));

            Assert.NotEmpty(groups);
            var g = groups.First(g => g.StatementCount >= 3);
            Assert.Equal(2, g.Occurrences.Count);
        }
    }

    // ── US-202: Changed integer literal still groups ──────────────────────────────

    [Fact]
    public async Task Changed_integer_literal_still_groups()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void First()
                {
                    int x = 42;
                    int y = 10;
                    System.Console.WriteLine(x + y);
                }

                public void Second()
                {
                    int x = 99;
                    int y = 7;
                    System.Console.WriteLine(x + y);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions());

            Assert.NotEmpty(groups);
            var g = groups.First(g => g.StatementCount == 3);
            Assert.Equal(2, g.Occurrences.Count);
        }
    }

    // ── US-202: Changed string literal still groups ───────────────────────────────

    [Fact]
    public async Task Changed_string_literal_still_groups()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void First()
                {
                    string msg = "hello";
                    System.Console.WriteLine(msg);
                    System.Console.WriteLine(msg + "!");
                }

                public void Second()
                {
                    string msg = "world";
                    System.Console.WriteLine(msg);
                    System.Console.WriteLine(msg + "?");
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions());

            Assert.NotEmpty(groups);
            var g = groups.First(g => g.StatementCount == 3);
            Assert.Equal(2, g.Occurrences.Count);
        }
    }

    // ── US-202: Different control-flow shape does NOT group ───────────────────────

    [Fact]
    public async Task Different_control_flow_does_not_group()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void WithIf(bool flag)
                {
                    int x = 1;
                    if (flag)
                    {
                        System.Console.WriteLine(x);
                    }
                    System.Console.WriteLine(x + 1);
                }

                public void WithWhile(bool flag)
                {
                    int x = 1;
                    while (flag)
                    {
                        System.Console.WriteLine(x);
                    }
                    System.Console.WriteLine(x + 1);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions(MinStatements: 3));

            // if-body and while-body differ in SyntaxKind, so these should not group.
            Assert.DoesNotContain(groups, g => g.Occurrences.Count >= 2 && g.StatementCount == 3);
        }
    }

    // ── US-202: Different invocation target does NOT group ────────────────────────

    [Fact]
    public async Task Different_invocation_target_does_not_group()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void CallFoo()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }

                public void CallBar()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.Write(x + y);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions());

            // WriteLine and Write are different member names — must not group.
            Assert.Empty(groups);
        }
    }

    // ── US-202: Whitespace/comment-only difference groups ────────────────────────

    [Fact]
    public async Task Whitespace_and_comment_difference_still_groups()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void First()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }

                public void Second()
                {
                    // leading comment
                    int x   =   1;   // trailing comment
                    int y=2;
                    System.Console.WriteLine( x+y );
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions());

            Assert.NotEmpty(groups);
            var g = groups.First(g => g.StatementCount == 3);
            Assert.Equal(2, g.Occurrences.Count);
        }
    }

    // ── US-203: Overlap suppression — only largest window reported ────────────────

    [Fact]
    public async Task Overlap_suppression_keeps_only_largest_window()
    {
        // Both methods contain a 5-statement clone. The bottom-up algorithm may also
        // report smaller sub-windows (3-stmt, 4-stmt). After overlap suppression, only
        // the 5-statement group should survive.
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void First()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                    int z = 3;
                    System.Console.WriteLine(z);
                }

                public void Second()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                    int z = 3;
                    System.Console.WriteLine(z);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions(MinStatements: 3));

            // Only the maximal (5-stmt) group should remain; sub-windows should be suppressed.
            Assert.DoesNotContain(groups, g => g.StatementCount < 5);
            var maxGroup = groups.Single(g => g.StatementCount == 5);
            Assert.Equal(2, maxGroup.Occurrences.Count);
        }
    }

    // ── US-203: Sub-block appearing standalone (not covered) is kept ──────────────

    [Fact]
    public async Task Sub_block_standalone_occurrence_is_not_suppressed()
    {
        // MethodA and MethodB share a 5-statement block.
        // MethodC contains only the first 3 statements of that block (no larger covering group).
        // The 3-statement group has an occurrence in MethodC that is NOT covered by the 5-stmt group,
        // so it must NOT be suppressed.
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void MethodA()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                    int z = 3;
                    System.Console.WriteLine(z);
                }

                public void MethodB()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                    int z = 3;
                    System.Console.WriteLine(z);
                }

                public void MethodC()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions(MinStatements: 3));

            // The 3-statement group should still appear because MethodC's occurrence is NOT covered.
            Assert.Contains(groups, g => g.StatementCount == 3);
        }
    }

    // ── US-203: Score-based ranking is stable across two runs ────────────────────

    [Fact]
    public async Task Score_ranking_is_stable_across_two_runs()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void First()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }

                public void Second()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var run1 = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions());
            var run2 = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions());

            Assert.Equal(run1.Count, run2.Count);
            for (var i = 0; i < run1.Count; i++)
            {
                Assert.Equal(run1[i].Score, run2[i].Score);
                Assert.Equal(run1[i].StatementCount, run2[i].StatementCount);
                Assert.Equal(run1[i].Occurrences.Count, run2[i].Occurrences.Count);
            }
        }
    }

    // ── US-203: Two runs produce identical group ids ──────────────────────────────

    [Fact]
    public async Task Two_runs_produce_identical_group_ids()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void First()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }

                public void Second()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var run1 = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions());
            var run2 = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions());

            Assert.Equal(run1.Count, run2.Count);
            var ids1 = run1.Select(g => g.GroupId).OrderBy(id => id).ToList();
            var ids2 = run2.Select(g => g.GroupId).OrderBy(id => id).ToList();
            Assert.Equal(ids1, ids2);

            // GroupId format: GRP-{stmtCount}-{8 hex chars}
            foreach (var id in ids1)
                Assert.Matches(@"^GRP-\d+-[0-9a-f]{8}$", id);
        }
    }

    // ── US-204: --scope namespace prefix filters occurrences ─────────────────────

    [Fact]
    public async Task Scope_namespace_returns_only_groups_in_that_namespace()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services/A.cs", """
            namespace MyApp.Services;

            public class ServiceA
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);
        project.AddSource("Services/B.cs", """
            namespace MyApp.Services;

            public class ServiceB
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);
        project.AddSource("Other/C.cs", """
            namespace MyApp.Other;

            public class OtherC
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions(Scope: "MyApp.Services"));

            Assert.NotEmpty(groups);
            foreach (var g in groups)
                Assert.All(g.Occurrences, o => Assert.Contains("Services", o.FilePath));
        }
    }

    // ── US-204: --scope project name filters to that project ─────────────────────

    [Fact]
    public async Task Scope_project_name_filters_to_that_project()
    {
        using var builder = new AdhocProjectBuilder("ProjectAlpha");
        builder.AddSource("A.cs", """
            namespace ProjectAlpha;

            public class A
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);
        builder.AddSource("B.cs", """
            namespace ProjectAlpha;

            public class B
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);

        var beta = builder.AddProject("ProjectBeta");
        beta.AddSource("C.cs", """
            namespace ProjectBeta;

            public class C
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);

        var (workspace, sln) = builder.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions(Scope: "ProjectAlpha"));

            Assert.NotEmpty(groups);
            foreach (var g in groups)
                Assert.All(g.Occurrences, o => Assert.Contains("ProjectAlpha", o.FilePath));
        }
    }

    // ── US-204: --include-tests false excludes test projects ─────────────────────

    [Fact]
    public async Task IncludeTests_false_excludes_test_projects()
    {
        // Two identical methods in a project named "MyApp.Tests" — should not be reported
        // when IncludeTests=false (the default).
        using var builder = new AdhocProjectBuilder("MyApp.Tests");
        builder.AddSource("A.cs", """
            namespace MyApp.Tests;

            public class TestA
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);
        builder.AddSource("B.cs", """
            namespace MyApp.Tests;

            public class TestB
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);

        var (workspace, sln) = builder.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions(IncludeTests: false));

            Assert.Empty(groups);
        }
    }

    // ── US-204: --include-tests true includes test projects ──────────────────────

    [Fact]
    public async Task IncludeTests_true_includes_test_projects()
    {
        using var builder = new AdhocProjectBuilder("MyApp.Tests");
        builder.AddSource("A.cs", """
            namespace MyApp.Tests;

            public class TestA
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);
        builder.AddSource("B.cs", """
            namespace MyApp.Tests;

            public class TestB
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);

        var (workspace, sln) = builder.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions(IncludeTests: true));

            Assert.NotEmpty(groups);
            var g = groups.First(gr => gr.StatementCount == 3);
            Assert.Equal(2, g.Occurrences.Count);
        }
    }

    // ── US-204: --max-statements filters out windows exceeding the cap ────────────

    [Fact]
    public async Task MaxStatements_filters_out_windows_exceeding_cap()
    {
        // Both methods have a 5-statement duplicate. With max-statements=3, the algorithm
        // should stop expanding at 3 statements and never produce a 5-statement group.
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void First()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                    int z = 3;
                    System.Console.WriteLine(z);
                }

                public void Second()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                    int z = 3;
                    System.Console.WriteLine(z);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions(MaxStatements: 3));

            Assert.DoesNotContain(groups, g => g.StatementCount > 3);
        }
    }
}
