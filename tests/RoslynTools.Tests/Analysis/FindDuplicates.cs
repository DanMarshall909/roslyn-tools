using System.Text.Json;
using RoslynTools.Commands.Results;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Tests.Analysis;

public class FindDuplicates
{
    // ── Exact duplicates within a single file detected ────────────────────────────

    [Fact]
    public async Task detects_exact_duplicates_inside_one_file()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            Assert.NotEmpty(groups);
            var g = groups[0];
            Assert.Equal(2, g.Occurrences.Count);
            Assert.Equal(3, g.StatementCount);
        }
    }

    // ── Exact duplicates across files detected ────────────────────────────────────

    [Fact]
    public async Task detects_exact_duplicates_across_files()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

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
    public async Task returns_empty_when_no_duplicates()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            Assert.Empty(groups);
        }
    }

    // ── min-statements filter ─────────────────────────────────────────────────────

    [Fact]
    public async Task handles_min_statements_filters_shorter_duplicates()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(5));

            Assert.Empty(groups);
        }
    }

    // ── min-occurrences filter ────────────────────────────────────────────────────

    [Fact]
    public async Task handles_min_occurrences_filters_groups_with_too_few_occurrences()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(MinOccurrences: 3));

            Assert.Empty(groups);
        }
    }

    // ── Bottom-up: only maximal clone reported, not sub-sequences ────────────────

    [Fact]
    public async Task reports_only_maximal_clone_not_sub_sequences()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(3));

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
    public async Task flags_partial_expansion_when_some_occurrences_extend()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(3));

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
    public async Task does_not_report_boilerplate_only_blocks()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(1));

            Assert.Empty(groups);
        }
    }

    // ── US-202: Renamed local variable still groups ───────────────────────────────

    [Fact]
    public async Task handles_renamed_local_variable_still_groups()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            Assert.NotEmpty(groups);
            var g = groups.First(g => g.StatementCount == 3);
            Assert.Equal(2, g.Occurrences.Count);
        }
    }

    // ── US-202: Renamed parameter still groups ────────────────────────────────────

    [Fact]
    public async Task handles_renamed_parameter_still_groups()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(3));

            Assert.NotEmpty(groups);
            var g = groups.First(g => g.StatementCount >= 3);
            Assert.Equal(2, g.Occurrences.Count);
        }
    }

    // ── US-202: Changed integer literal still groups ──────────────────────────────

    [Fact]
    public async Task handles_changed_integer_literal_still_groups()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            Assert.NotEmpty(groups);
            var g = groups.First(g => g.StatementCount == 3);
            Assert.Equal(2, g.Occurrences.Count);
        }
    }

    // ── US-202: Changed string literal still groups ───────────────────────────────

    [Fact]
    public async Task handles_changed_string_literal_still_groups()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            Assert.NotEmpty(groups);
            var g = groups.First(g => g.StatementCount == 3);
            Assert.Equal(2, g.Occurrences.Count);
        }
    }

    // ── US-202: Different control-flow shape does NOT group ───────────────────────

    [Fact]
    public async Task handles_different_control_flow_does_not_group()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(3));

            // if-body and while-body differ in SyntaxKind, so these should not group.
            Assert.DoesNotContain(groups, g => g.Occurrences.Count >= 2 && g.StatementCount == 3);
        }
    }

    // ── US-202: Different invocation target does NOT group ────────────────────────

    [Fact]
    public async Task handles_different_invocation_target_does_not_group()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            // WriteLine and Write are different member names — must not group.
            Assert.Empty(groups);
        }
    }

    // ── US-202: Whitespace/comment-only difference groups ────────────────────────

    [Fact]
    public async Task groups_blocks_despite_whitespace_and_comment_differences()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            Assert.NotEmpty(groups);
            var g = groups.First(g => g.StatementCount == 3);
            Assert.Equal(2, g.Occurrences.Count);
        }
    }

    // ── US-203: Overlap suppression — only largest window reported ────────────────

    [Fact]
    public async Task keeps_only_largest_window_when_suppressing_overlaps()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(3));

            // Only the maximal (5-stmt) group should remain; sub-windows should be suppressed.
            Assert.DoesNotContain(groups, g => g.StatementCount < 5);
            var maxGroup = groups.Single(g => g.StatementCount == 5);
            Assert.Equal(2, maxGroup.Occurrences.Count);
        }
    }

    // ── US-203: Sub-block appearing standalone (not covered) is kept ──────────────

    [Fact]
    public async Task sub_block_standalone_occurrence_is_not_suppressed()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(3));

            // The 3-statement group should still appear because MethodC's occurrence is NOT covered.
            Assert.Contains(groups, g => g.StatementCount == 3);
        }
    }

    // ── US-203: Score-based ranking is stable across two runs ────────────────────

    [Fact]
    public async Task score_ranking_is_stable_across_two_runs()
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
            var run1 = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            var run2 = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

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
    public async Task two_runs_produce_identical_group_ids()
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
            var run1 = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            var run2 = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

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
    public async Task returns_only_groups_in_namespace_scope()
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
            var groups =
                await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(Scope: "MyApp.Services"));

            Assert.NotEmpty(groups);
            foreach (var g in groups)
                Assert.All(g.Occurrences, o => Assert.Contains("Services", o.FilePath));
        }
    }

    // ── US-204: --scope project name filters to that project ─────────────────────

    [Fact]
    public async Task filters_to_project_name_scope()
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
            var groups =
                await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(Scope: "ProjectAlpha"));

            Assert.NotEmpty(groups);
            foreach (var g in groups)
                Assert.All(g.Occurrences, o => Assert.Contains("ProjectAlpha", o.FilePath));
        }
    }

    // ── US-204: --include-tests false excludes test projects ─────────────────────

    [Fact]
    public async Task includes_tests_false_excludes_test_projects()
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
            var groups =
                await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(IncludeTests: false));

            Assert.Empty(groups);
        }
    }

    // ── US-204: --include-tests true includes test projects ──────────────────────

    [Fact]
    public async Task includes_tests_true_includes_test_projects()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(IncludeTests: true));

            Assert.NotEmpty(groups);
            var g = groups.First(gr => gr.StatementCount == 3);
            Assert.Equal(2, g.Occurrences.Count);
        }
    }

    // ── US-204: --max-statements filters out windows exceeding the cap ────────────

    [Fact]
    public async Task handles_max_statements_filters_out_windows_exceeding_cap()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(MaxStatements: 3));

            Assert.DoesNotContain(groups, g => g.StatementCount > 3);
        }
    }

    // ── US-205: JSON output — 2-group result deserializes correctly ───────────────

    [Fact]
    public async Task emits_json_output_two_groups_deserializes_correctly()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            Assert.NotEmpty(groups);

            var json = CaptureJson(groups, null);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("structural", root.GetProperty("mode").GetString());
            var groupsArr = root.GetProperty("groups");
            Assert.True(groupsArr.GetArrayLength() >= 1);

            var g = groupsArr[0];
            Assert.Equal("structural", g.GetProperty("mode").GetString());
            // extractability is now analysed and must be a known level, not "unknown"
            var ext = g.GetProperty("extractability").GetString()!;
            Assert.True(ext is "extractable" or "likely_extractable" or "discovery_only",
                $"Expected a known extractability level, got: {ext}");
            Assert.True(g.GetProperty("score").GetDouble() > 0);
            Assert.True(g.GetProperty("occurrenceCount").GetInt32() >= 2);
            Assert.True(g.GetProperty("preview").GetString()!.Length > 0);

            var occs = g.GetProperty("occurrences");
            Assert.True(occs.GetArrayLength() >= 2);

            var occ = occs[0];
            Assert.True(occ.GetProperty("file").GetString()!.Length > 0);
            Assert.True(occ.GetProperty("line").GetInt32() > 0);
            Assert.True(occ.GetProperty("endLine").GetInt32() > 0);
            Assert.True(occ.GetProperty("type").GetString()!.Length > 0);
            Assert.True(occ.GetProperty("member").GetString()!.Length > 0);

            var checks = g.GetProperty("checks");
            // checks is now populated with extractability analysis results
            Assert.Equal(JsonValueKind.Array, checks.ValueKind);
        }
    }

    // ── US-205: JSON output — empty result emits correct shape ───────────────────

    [Fact]
    public async Task emits_json_output_empty_result_emits_correct_shape()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            Assert.Empty(groups);

            var json = CaptureJson(groups, null);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("structural", root.GetProperty("mode").GetString());
            Assert.Equal(0, root.GetProperty("groups").GetArrayLength());
        }
    }

    // ── US-205: JSON output — groupId is stable across two runs ─────────────────

    [Fact]
    public async Task emits_json_output_group_id_is_stable_across_two_runs()
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
            var run1 = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            var run2 = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            var json1 = CaptureJson(run1, null);
            var json2 = CaptureJson(run2, null);

            var ids1 = JsonDocument.Parse(json1).RootElement
                .GetProperty("groups").EnumerateArray()
                .Select(g => g.GetProperty("groupId").GetString())
                .OrderBy(id => id)
                .ToList();

            var ids2 = JsonDocument.Parse(json2).RootElement
                .GetProperty("groups").EnumerateArray()
                .Select(g => g.GetProperty("groupId").GetString())
                .OrderBy(id => id)
                .ToList();

            Assert.Equal(ids1, ids2);
        }
    }

    // ── US-205: JSON output — occurrences use relative paths ────────────────────

    [Fact]
    public async Task emits_json_output_uses_relative_paths_when_solution_dir_provided()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            Assert.NotEmpty(groups);

            // Use the project directory as the "solution dir" so relative paths are computed.
            var solutionDir = project.Directory;
            var json = CaptureJson(groups, solutionDir);
            var doc = JsonDocument.Parse(json);

            var occs = doc.RootElement.GetProperty("groups")[0].GetProperty("occurrences");
            foreach (var occ in occs.EnumerateArray())
            {
                var file = occ.GetProperty("file").GetString()!;
                // Relative path must not be an absolute path
                Assert.False(Path.IsPathRooted(file), $"Expected relative path, got: {file}");
            }
        }
    }

    // ── US-205: JSON output — falls back to filename when no solution dir ────────

    [Fact]
    public async Task emits_json_output_falls_back_to_filename_without_solution_dir()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            Assert.NotEmpty(groups);

            var json = CaptureJson(groups, null);
            var doc = JsonDocument.Parse(json);

            var occs = doc.RootElement.GetProperty("groups")[0].GetProperty("occurrences");
            foreach (var occ in occs.EnumerateArray())
            {
                var file = occ.GetProperty("file").GetString()!;
                // Should be just a filename, not an absolute path
                Assert.Equal(Path.GetFileName(file), file);
            }
        }
    }

    // ── US-205: Plain-text output still works when --json not set ────────────────

    [Fact]
    public async Task emits_plain_text_output_without_json_flag()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            Assert.NotEmpty(groups);

            var output = CaptureText(groups);
            Assert.Contains("Group 1", output);
            Assert.Contains("preview:", output);
        }
    }

    // ── US-207: Extraction-readiness diagnostics ──────────────────────────────────

    [Fact]
    public async Task handles_fully_extractable_group_is_labelled_extractable()
    {
        // Simple 3-statement block with no control-flow escapes, no ref/out, consistent state.
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            Assert.NotEmpty(groups);
            var g = groups.First(g => g.StatementCount == 3);
            Assert.Equal("extractable", g.Extractability);
            Assert.NotNull(g.Checks);
            Assert.True(g.Checks.Count > 0);
            Assert.All(g.Checks, c => Assert.Equal("pass", c.Status));
        }
    }

    [Fact]
    public async Task handles_group_with_return_inside_block_is_discovery_only_with_no_control_flow_escape_fail()
    {
        // A 3-statement block where one of the statements is a return — extractability should fail.
        // ValueReturn (trailing return with a value) is not yet supported by MethodExtractor;
        // only ExtractAsBoolGuard (single guard if-return) is wired up. Until ExtractWithReturnType
        // is implemented, value-return groups must remain discovery_only so the status is honest.
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
                                  namespace TestApp;

                                  public class A
                                  {
                                      public int First()
                                      {
                                          int x = 1;
                                          int y = 2;
                                          return x + y;
                                      }

                                      public int Second()
                                      {
                                          int x = 1;
                                          int y = 2;
                                          return x + y;
                                      }
                                  }
                                  """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(3));

            Assert.NotEmpty(groups);
            var g = groups.First(g => g.StatementCount == 3);
            Assert.Equal("discovery_only", g.Extractability);
            Assert.NotNull(g.Checks);
            var failCheck = g.Checks.FirstOrDefault(c => c.Name == "no-control-flow-escape");
            Assert.NotNull(failCheck);
            Assert.Equal("fail", failCheck.Status);
        }
    }

    [Fact]
    public async Task handles_group_with_ref_out_param_is_discovery_only_with_no_ref_out_params_fail()
    {
        // A block that passes a ref argument should fail the no-ref-out-params check.
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
                                  namespace TestApp;

                                  public class A
                                  {
                                      private static void Helper(ref int v) { v++; }

                                      public void First()
                                      {
                                          int x = 1;
                                          int y = 2;
                                          Helper(ref x);
                                      }

                                      public void Second()
                                      {
                                          int x = 1;
                                          int y = 2;
                                          Helper(ref x);
                                      }
                                  }
                                  """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            Assert.NotEmpty(groups);
            var g = groups.First(g => g.StatementCount >= 3);
            Assert.Equal("discovery_only", g.Extractability);
            Assert.NotNull(g.Checks);
            var failCheck = g.Checks.FirstOrDefault(c => c.Name == "no-ref-out-params");
            Assert.NotNull(failCheck);
            Assert.Equal("fail", failCheck.Status);
        }
    }

    [Fact]
    public async Task handles_group_with_differing_captured_fields_is_likely_extractable_with_consistent_captured_state_warn()
    {
        // Two classes each have a field named `_count`, but they are distinct field symbols
        // (A._count vs B._count). Both methods access `_count` with the same syntax, so they
        // group canonically. The consistent-captured-state check detects the differing FQNs.
        using var builder = new AdhocProjectBuilder("CaptureTest");
        builder.AddSource("A.cs", """
                                  namespace CaptureTest;

                                  public class A
                                  {
                                      private int _count = 5;

                                      public void Run()
                                      {
                                          int x = 1;
                                          int y = _count;
                                          System.Console.WriteLine(x + y);
                                      }
                                  }
                                  """);
        builder.AddSource("B.cs", """
                                  namespace CaptureTest;

                                  public class B
                                  {
                                      private int _count = 10;

                                      public void Run()
                                      {
                                          int x = 1;
                                          int y = _count;
                                          System.Console.WriteLine(x + y);
                                      }
                                  }
                                  """);

        var (workspace, sln) = builder.Build();
        using (workspace)
        {
            // "CaptureTest" ends with "Test" so it would be excluded as a test project;
            // use IncludeTests:true so the project is scanned.
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(IncludeTests: true));

            Assert.NotEmpty(groups);
            var g = groups.First(g => g.StatementCount >= 3);
            // consistent-captured-state should warn (A._count vs B._count are different symbols)
            Assert.NotNull(g.Checks);
            var captureCheck = g.Checks.FirstOrDefault(c => c.Name == "consistent-captured-state");
            Assert.NotNull(captureCheck);
            Assert.Equal("warn", captureCheck.Status);
            // Overall: at least likely_extractable (warn present, not extractable)
            Assert.NotEqual("extractable", g.Extractability);
        }
    }

    [Fact]
    public async Task emits_json_output_includes_extractability_and_checks()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            Assert.NotEmpty(groups);

            var json = CaptureJson(groups, null);
            var doc = JsonDocument.Parse(json);
            var g = doc.RootElement.GetProperty("groups")[0];

            // extractability must be a non-empty string (extractable/likely_extractable/discovery_only)
            var ext = g.GetProperty("extractability").GetString()!;
            Assert.True(ext.Length > 0 && ext != "unknown", $"Expected known extractability, got: {ext}");

            // checks must be an array (may be empty for extractable groups but structure must be present)
            var checks = g.GetProperty("checks");
            Assert.Equal(JsonValueKind.Array, checks.ValueKind);

            // Each check entry (if any) must have name, status, and detail fields
            foreach (var check in checks.EnumerateArray())
            {
                Assert.True(check.TryGetProperty("name", out _));
                Assert.True(check.TryGetProperty("status", out _));
                Assert.True(check.TryGetProperty("detail", out _));
            }
        }
    }

    [Fact]
    public async Task emits_plain_text_output_with_extractability_label()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            Assert.NotEmpty(groups);

            var output = CaptureText(groups);
            Assert.Contains("extractable=", output);
        }
    }

    // ── Output format: forward-slash paths and line ranges ────────────────────────

    [Fact]
    public async Task emits_plain_text_output_with_forward_slash_paths()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            Assert.NotEmpty(groups);

            var output = CaptureText(groups);
            var occurrenceLines = output.Split('\n').Where(l =>
                l.TrimStart().StartsWith("1.", StringComparison.Ordinal) ||
                l.TrimStart().StartsWith("2.", StringComparison.Ordinal));
            Assert.All(occurrenceLines, line => Assert.DoesNotContain('\\', line));
        }
    }

    [Fact]
    public async Task emits_plain_text_output_with_line_range()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            Assert.NotEmpty(groups);

            var output = CaptureText(groups);
            // Each occurrence line should contain a startLine-endLine range like ":7-9"
            var occurrenceLines = output.Split('\n').Where(l =>
                l.TrimStart().StartsWith("1.", StringComparison.Ordinal) ||
                l.TrimStart().StartsWith("2.", StringComparison.Ordinal)).ToList();
            Assert.All(occurrenceLines, line => Assert.Matches(@":\d+-\d+", line));
        }
    }

    [Fact]
    public async Task occurrence_end_line_is_greater_than_or_equal_to_start_line()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            Assert.NotEmpty(groups);

            Assert.All(groups.SelectMany(g => g.Occurrences), o => Assert.True(o.EndLine >= o.StartLine));
        }
    }

    [Fact]
    public async Task emits_json_link_field_uses_forward_slash_path_with_start_line()
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
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            Assert.NotEmpty(groups);

            var json = JsonDocument.Parse(CaptureJson(groups, null));
            var occurrences = json.RootElement.GetProperty("groups")[0].GetProperty("occurrences");
            foreach (var occ in occurrences.EnumerateArray())
            {
                var link = occ.GetProperty("link").GetString()!;
                Assert.DoesNotContain('\\', link);
                Assert.Matches(@":\d+$", link);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static string CaptureJson(IReadOnlyList<DuplicateGroup> groups, string? solutionDir)
    {
        var sw = new StringWriter();
        var result = FindDuplicatesResult.FromGroups(groups, solutionDir);
        var prevFormat = RtContext.Format;
        RtContext.Format = OutputFormat.Json;
        try
        {
            RtSerializer.Write(result, sw);
        }
        finally
        {
            RtContext.Format = prevFormat;
        }

        return sw.ToString().Trim();
    }

    // ── AC #7: Guard-return groups are likely_extractable, not discovery_only ────────

    [Fact]
    public async Task handles_guard_return_group_is_likely_extractable_not_discovery_only()
    {
        // Arrange: two methods whose only duplicate block is a single guard clause:
        //   if (string.IsNullOrWhiteSpace(input)) return false;
        // ControlFlowUnwinder classifies a single if-return as ExtractAsBoolGuard.
        // The no-control-flow-escape check must return "warn" (not "fail") so the group
        // surfaces as likely_extractable rather than discovery_only.
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
                                  namespace TestApp;

                                  public class ServiceA
                                  {
                                      public bool ValidateAndProcess(string? input)
                                      {
                                          if (string.IsNullOrWhiteSpace(input))
                                              return false;
                                          System.Console.WriteLine(input);
                                          System.Console.WriteLine(input.Length);
                                          return true;
                                      }
                                  }

                                  public class ServiceB
                                  {
                                      public bool CheckAndLog(string? value)
                                      {
                                          if (string.IsNullOrWhiteSpace(value))
                                              return false;
                                          int len = value.Length;
                                          System.Console.WriteLine(len * 2);
                                          return true;
                                      }
                                  }
                                  """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(1));

            // The single guard clause `if (IsNullOrWhiteSpace...) return false;` is a
            // 1-statement duplicate group. It must surface as likely_extractable.
            var guardGroup = groups.FirstOrDefault(g =>
                g.FullBlock.Contains("IsNullOrWhiteSpace") &&
                (g.Extractability == "likely_extractable" || g.Extractability == "extractable"));

            Assert.NotNull(guardGroup);

            // The no-control-flow-escape check must be warn for ExtractAsBoolGuard
            var cfCheck = guardGroup.Checks?.FirstOrDefault(c => c.Name == "no-control-flow-escape");
            Assert.NotNull(cfCheck);
            Assert.Equal("warn", cfCheck!.Status);
            Assert.Contains("guard pattern", cfCheck.Detail, StringComparison.OrdinalIgnoreCase);

            // Overall extractability must not be discovery_only
            Assert.NotEqual("discovery_only", guardGroup.Extractability);
        }
    }

    // ── US-047: Multi-output blocks are likely_extractable (tuple returns supported) ─

    [Fact]
    public async Task handles_multi_output_block_is_likely_extractable_not_discovery_only()
    {
        // MethodExtractor supports tuple returns for 2+ outward variables.
        // The single-output check must return "warn" (not "fail") so groups
        // with multi-output blocks surface as likely_extractable, not discovery_only.
        //
        // Design: the ONLY duplicate across the two methods is the 2-statement compute
        // block. The surrounding code is intentionally different so the wider window is
        // not itself a duplicate — the 2-stmt group is not suppressed.
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
                                  namespace TestApp;

                                  public class ServiceA
                                  {
                                      public void ProcessA(int x)
                                      {
                                          var lo = x - 1;
                                          var hi = x + 1;
                                          System.Console.WriteLine(lo + hi);
                                      }

                                      public void ProcessB(int n)
                                      {
                                          var lo = n - 1;
                                          var hi = n + 1;
                                          Consume(lo, hi);
                                      }

                                      private static void Consume(int a, int b) { }
                                  }
                                  """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(2));

            // The 2-statement compute block (var lo / var hi) is the only duplicate;
            // the callers differ so no larger window is a duplicate, so the 2-stmt group
            // survives suppression.
            Assert.NotEmpty(groups);
            var multiOutputGroup = groups[0];

            // Verify this is indeed the compute block
            Assert.Equal(2, multiOutputGroup.StatementCount);

            // The single-output check must be warn (tuple-extractable), not fail
            var soCheck = multiOutputGroup.Checks?.FirstOrDefault(c => c.Name == "single-output");
            Assert.NotNull(soCheck);
            Assert.Equal("warn", soCheck!.Status);

            // Overall extractability must not be discovery_only
            Assert.NotEqual("discovery_only", multiOutputGroup.Extractability);
        }
    }

    private static string CaptureText(IReadOnlyList<DuplicateGroup> groups)
    {
        var sw = new StringWriter();
        var result = FindDuplicatesResult.FromGroups(groups, null);
        var prevFormat = RtContext.Format;
        RtContext.Format = OutputFormat.Text;
        try
        {
            RtSerializer.Write(result, sw);
        }
        finally
        {
            RtContext.Format = prevFormat;
        }

        return sw.ToString();
    }
}
