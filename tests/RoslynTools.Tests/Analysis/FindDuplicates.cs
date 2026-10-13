using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            groups.ShouldNotBeEmpty();
            var g = groups[0];
            g.Occurrences.Count.ShouldBe(2);
            g.StatementCount.ShouldBe(3);
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            groups.ShouldNotBeEmpty();
            var g = groups.First(g => g.StatementCount == 3);
            g.Occurrences.Count.ShouldBe(2);
            var files = g.Occurrences.Select(o => Path.GetFileName(o.FilePath)).ToHashSet();
            files.ShouldContain("A.cs");
            files.ShouldContain("B.cs");
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            groups.ShouldBeEmpty();
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(5));

            groups.ShouldBeEmpty();
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(MinOccurrences: 3));

            groups.ShouldBeEmpty();
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(3));

            // The 5-statement clone is the maximal block; sub-sequences (3-stmt, 4-stmt windows)
            // should not produce separate reported groups since all their occurrences are subsumed.
            groups.ShouldNotBeEmpty();
            var maximalGroup = groups.OrderByDescending(g => g.StatementCount).First();
            maximalGroup.StatementCount.ShouldBe(5);
            maximalGroup.Occurrences.Count.ShouldBe(2);
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(3));

            // The [s1,s2,s3] group appears in all 3 methods with PartiallyExpandable=true.
            // Use 3-occurrence count + PartiallyExpandable as the discriminator since there may be
            // other 3-statement groups (e.g. [s2,s3,s4] from MethodA and MethodC).
            var threeStmtGroup = groups.FirstOrDefault(g => g.StatementCount == 3 && g.Occurrences.Count == 3);
            threeStmtGroup.ShouldNotBeNull();
            Assert.True(threeStmtGroup.PartiallyExpandable,
                "3-statement group should be flagged PartiallyExpandable because some occurrences have an identical 4th statement");

            var fourStmtGroup = groups.FirstOrDefault(g => g.StatementCount == 4);
            fourStmtGroup.ShouldNotBeNull();
            fourStmtGroup.Occurrences.Count.ShouldBe(2);
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(1));

            groups.ShouldBeEmpty();
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            groups.ShouldNotBeEmpty();
            var g = groups.First(g => g.StatementCount == 3);
            g.Occurrences.Count.ShouldBe(2);
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(3));

            groups.ShouldNotBeEmpty();
            var g = groups.First(g => g.StatementCount >= 3);
            g.Occurrences.Count.ShouldBe(2);
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            groups.ShouldNotBeEmpty();
            var g = groups.First(g => g.StatementCount == 3);
            g.Occurrences.Count.ShouldBe(2);
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            groups.ShouldNotBeEmpty();
            var g = groups.First(g => g.StatementCount == 3);
            g.Occurrences.Count.ShouldBe(2);
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(3));

            // if-body and while-body differ in SyntaxKind, so these should not group.
            groups.ShouldNotContain(g => g.Occurrences.Count >= 2 && g.StatementCount == 3);
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            // WriteLine and Write are different member names — must not group.
            groups.ShouldBeEmpty();
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            groups.ShouldNotBeEmpty();
            var g = groups.First(g => g.StatementCount == 3);
            g.Occurrences.Count.ShouldBe(2);
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(3));

            // Only the maximal (5-stmt) group should remain; sub-windows should be suppressed.
            groups.ShouldNotContain(g => g.StatementCount < 5);
            var maxGroup = groups.Single(g => g.StatementCount == 5);
            maxGroup.Occurrences.Count.ShouldBe(2);
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(3));

            // The 3-statement group should still appear because MethodC's occurrence is NOT covered.
            groups.ShouldContain(g => g.StatementCount == 3);
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var run1 = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            var run2 = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            run2.Count.ShouldBe(run1.Count);
            for (var i = 0; i < run1.Count; i++)
            {
                run2[i].Score.ShouldBe(run1[i].Score);
                run2[i].StatementCount.ShouldBe(run1[i].StatementCount);
                run2[i].Occurrences.Count.ShouldBe(run1[i].Occurrences.Count);
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var run1 = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            var run2 = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            run2.Count.ShouldBe(run1.Count);
            var ids1 = run1.Select(g => g.GroupId).OrderBy(id => id).ToList();
            var ids2 = run2.Select(g => g.GroupId).OrderBy(id => id).ToList();
            ids2.ShouldBe(ids1);

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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups =
                await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(Scope: "MyApp.Services"));

            groups.ShouldNotBeEmpty();
            foreach (var g in groups)
                Assert.All(g.Occurrences, o => o.FilePath.ShouldContain("Services", Case.Sensitive));
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

        var (workspace, sln) = await BuildFixtureAsync(builder);
        using (workspace)
        {
            var groups =
                await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(Scope: "ProjectAlpha"));

            groups.ShouldNotBeEmpty();
            foreach (var g in groups)
                Assert.All(g.Occurrences, o => o.FilePath.ShouldContain("ProjectAlpha", Case.Sensitive));
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

        var (workspace, sln) = await BuildFixtureAsync(builder);
        using (workspace)
        {
            var groups =
                await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(IncludeTests: false));

            groups.ShouldBeEmpty();
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

        var (workspace, sln) = await BuildFixtureAsync(builder);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(IncludeTests: true));

            groups.ShouldNotBeEmpty();
            var g = groups.First(gr => gr.StatementCount == 3);
            g.Occurrences.Count.ShouldBe(2);
        }
    }

    [Fact]
    public async Task excludes_test_framework_projects_unless_explicitly_included()
    {
        const string source = """
                              public class Duplicates
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
                              """;
        var frameworkProjects = new[]
        {
            (ProjectName: "RoslynTools.Tests.Mcp", AssemblyName: "xunit.core"),
            (ProjectName: "ValidationFixtures", AssemblyName: "xunit.v3.core"),
            (ProjectName: "ApprovalFixtures", AssemblyName: "nunit.framework"),
            (ProjectName: "AcceptanceFixtures", AssemblyName: "Microsoft.VisualStudio.TestPlatform.TestFramework")
        };
        using var builder = new AdhocProjectBuilder("Production");
        builder.AddSource("Duplicates.cs", source);
        builder.AddProject("FilenameControl").AddSource("Duplicates.cs", source);
        builder.AddProject("Legacy.Tests").AddSource("Duplicates.cs", source);
        foreach (var (projectName, _) in frameworkProjects)
            builder.AddProject(projectName).AddSource("Duplicates.cs", source);

        var (workspace, solution) = await BuildFixtureAsync(builder);
        using (workspace)
        {
            // Opaque filenames require classification by the emitted assembly identity.
            foreach (var (projectName, assemblyName) in frameworkProjects)
            {
                var project = solution.Projects.Single(p => p.Name == projectName);
                solution = solution.AddMetadataReference(project.Id,
                    CreateAssemblyReference(assemblyName, Path.Combine(builder.Directory, projectName + ".dll")));
            }

            // A framework-looking filename alone must not hide production duplicates.
            var filenameControl = solution.Projects.Single(p => p.Name == "FilenameControl");
            solution = solution.AddMetadataReference(filenameControl.Id,
                CreateAssemblyReference("Production.Dependency", Path.Combine(builder.Directory, "xunit.core.dll")));

            var service = new FindDuplicatesService();
            var defaultGroups = await service.RunAsync(solution, new FindDuplicatesOptions());
            var defaultGroup = Assert.Single(defaultGroups);
            Assert.Equal(3, defaultGroup.StatementCount);
            foreach (var projectName in new[] { "Production", "FilenameControl" })
            {
                var path = Path.Combine(builder.Directory, projectName, "Duplicates.cs");
                Assert.Contains(defaultGroup.Occurrences, o => o.FilePath == path && o.ContainingMember == "First");
                Assert.Contains(defaultGroup.Occurrences, o => o.FilePath == path && o.ContainingMember == "Second");
            }

            var fallbackPath = Path.Combine(builder.Directory, "Legacy.Tests", "Duplicates.cs");
            Assert.DoesNotContain(defaultGroup.Occurrences, o => o.FilePath == fallbackPath);

            var includedGroups = await service.RunAsync(solution, new FindDuplicatesOptions(IncludeTests: true));
            var includedGroup = Assert.Single(includedGroups);
            Assert.Equal(3, includedGroup.StatementCount);
            var expectedPaths = frameworkProjects.Select(p => p.ProjectName)
                .Concat(["Production", "FilenameControl", "Legacy.Tests"])
                .Select(name => Path.Combine(builder.Directory, name, "Duplicates.cs"))
                .ToArray();
            var expectedOccurrences = expectedPaths
                .SelectMany(path => new[] { path + ":First", path + ":Second" })
                .Order(StringComparer.Ordinal);
            Assert.Equal(expectedOccurrences, includedGroup.Occurrences
                .Select(o => o.FilePath + ":" + o.ContainingMember)
                .Order(StringComparer.Ordinal));

            var repeatedGroups = await service.RunAsync(solution, new FindDuplicatesOptions(IncludeTests: true));
            Assert.Equal(CaptureJson(includedGroups, builder.Directory), CaptureJson(repeatedGroups, builder.Directory));

            var frameworkPaths = frameworkProjects
                .Select(p => Path.Combine(builder.Directory, p.ProjectName, "Duplicates.cs"))
                .ToHashSet(StringComparer.Ordinal);
            var leakedOccurrences = defaultGroups.SelectMany(g => g.Occurrences)
                .Where(o => frameworkPaths.Contains(o.FilePath))
                .ToArray();
            Assert.Empty(leakedOccurrences);
        }
    }

    [Fact]
    public async Task excludes_neutral_named_msbuild_test_projects_unless_explicitly_included()
    {
        const string source = """
                              public class Duplicates
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
                              """;
        var scenarios = new[]
        {
            (Directory: "ImportedTrue", IsTestProject: "true", ViaImport: true),
            (Directory: "DirectTrue", IsTestProject: "true", ViaImport: false),
            (Directory: "DirectFalse", IsTestProject: "false", ViaImport: false)
        };
        using var fixture = new WorkspaceLoaderTestProject();
        MsBuildFixture.Ensure();

        foreach (var scenario in scenarios)
        {
            var sln = fixture.AddSolution(scenario.Directory, "Metadata", "Catalog", source);
            var projectDirectory = Path.Combine(fixture.RootDir, scenario.Directory, "Catalog");
            var projectPath = Path.Combine(projectDirectory, "Catalog.csproj");
            var projectXml = await File.ReadAllTextAsync(projectPath);
            if (scenario.ViaImport)
            {
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Identity.props"),
                    "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
                projectXml = projectXml.Replace("</Project>",
                    "  <Import Project=\"Identity.props\" />\n</Project>", StringComparison.Ordinal);
            }
            else
            {
                projectXml = projectXml.Replace("</PropertyGroup>",
                    $"  <IsTestProject>{scenario.IsTestProject}</IsTestProject>\n  </PropertyGroup>",
                    StringComparison.Ordinal);
            }

            await File.WriteAllTextAsync(projectPath, projectXml);
            using (var projects = new Microsoft.Build.Evaluation.ProjectCollection())
            {
                var evaluated = projects.LoadProject(projectPath);
                Assert.Equal(scenario.IsTestProject, evaluated.GetPropertyValue("IsTestProject"));
            }

            var (workspace, solution) = await WorkspaceLoader.LoadAsync(sln.SolutionPath);
            using (workspace)
            {
                var project = Assert.Single(solution.Projects);
                Assert.Equal("Catalog", project.Name);
                var compilation = await project.GetCompilationAsync();
                Assert.NotNull(compilation);
                Assert.False(compilation.ReferencedAssemblyNames.Any(assembly => assembly.Name is
                    "xunit.core" or "xunit.v3.core" or "nunit.framework" or
                    "Microsoft.VisualStudio.TestPlatform.TestFramework"),
                    "The fixture must not reference a recognized test framework.");

                var sourcePath = Path.Combine(projectDirectory, "Marker.cs");
                var expected = new[] { sourcePath + ":First", sourcePath + ":Second" };
                var service = new FindDuplicatesService();
                var included = await service.RunAsync(solution, new FindDuplicatesOptions(IncludeTests: true));
                var includedGroup = Assert.Single(included);
                Assert.Equal(3, includedGroup.StatementCount);
                var occurrences = includedGroup.Occurrences
                    .Select(o => o.FilePath + ":" + o.ContainingMember)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                Assert.Equal(expected, occurrences);

                var repeated = await service.RunAsync(solution, new FindDuplicatesOptions(IncludeTests: true));
                Assert.Equal(CaptureJson(included, fixture.RootDir), CaptureJson(repeated, fixture.RootDir));

                var defaultGroups = await service.RunAsync(solution, new FindDuplicatesOptions());
                if (scenario.IsTestProject == "true")
                {
                    Assert.DoesNotContain(defaultGroups.SelectMany(g => g.Occurrences),
                        occurrence => occurrence.FilePath == sourcePath);
                }
                else
                {
                    var defaultGroup = Assert.Single(defaultGroups);
                    Assert.Equal(expected, defaultGroup.Occurrences
                        .Select(o => o.FilePath + ":" + o.ContainingMember)
                        .Order(StringComparer.Ordinal));
                }
            }
        }
    }

    [Fact]
    public async Task excludes_neutral_named_multitarget_projects_with_inner_test_metadata()
    {
        const string source = """
                              public class Duplicates
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
                              """;
        using var fixture = new WorkspaceLoaderTestProject();
        var sln = fixture.AddSolution("InnerMetadata", "Metadata", "Catalog", source);
        var projectDirectory = Path.Combine(fixture.RootDir, "InnerMetadata", "Catalog");
        var projectPath = Path.Combine(projectDirectory, "Catalog.csproj");
        await File.WriteAllTextAsync(projectPath, """
                                                 <Project Sdk="Microsoft.NET.Sdk">
                                                   <PropertyGroup>
                                                     <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
                                                     <IsTestProject>false</IsTestProject>
                                                     <IsTestProject Condition="'$(TargetFramework)' != '' and '$(CatalogValidation)' == 'true'">true</IsTestProject>
                                                   </PropertyGroup>
                                                 </Project>
                                                 """);

        MsBuildFixture.Ensure();
        using var workspace = Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create(
            new Dictionary<string, string> { ["CatalogValidation"] = "true" });
        var solution = await workspace.OpenSolutionAsync(sln.SolutionPath);
        Assert.Equal(2, solution.ProjectIds.Count);

        // Verify the evaluated fixture using the same global properties as its workspace.
        using (var projects = new Microsoft.Build.Evaluation.ProjectCollection(
                   new Dictionary<string, string>(workspace.Properties)))
        {
            var outer = projects.LoadProject(projectPath);
            Assert.Equal(string.Empty, outer.GetPropertyValue("TargetFramework"));
            Assert.Equal("false", outer.GetPropertyValue("IsTestProject"));
        }

        foreach (var targetFramework in new[] { "net8.0", "net10.0" })
        {
            var properties = new Dictionary<string, string>(workspace.Properties)
            {
                ["TargetFramework"] = targetFramework
            };
            using var projects = new Microsoft.Build.Evaluation.ProjectCollection(properties);
            var inner = projects.LoadProject(projectPath);
            Assert.Equal("true", inner.GetPropertyValue("IsTestProject"));
        }

        foreach (var project in solution.Projects)
        {
            Assert.Equal(projectPath, project.FilePath);
            Assert.StartsWith("Catalog", project.Name);
            var compilation = await project.GetCompilationAsync();
            Assert.NotNull(compilation);
            Assert.False(compilation.ReferencedAssemblyNames.Any(assembly => assembly.Name is
                "xunit.core" or "xunit.v3.core" or "nunit.framework" or
                "Microsoft.VisualStudio.TestPlatform.TestFramework"),
                "The fixture must not reference a recognized test framework.");
        }

        var sourcePath = Path.Combine(projectDirectory, "Marker.cs");
        // Both compilation contexts contribute the two source occurrences when included.
        var expected = new[]
        {
            sourcePath + ":First", sourcePath + ":First",
            sourcePath + ":Second", sourcePath + ":Second"
        };
        var service = new FindDuplicatesService();
        var included = await service.RunAsync(solution, new FindDuplicatesOptions(IncludeTests: true));
        var includedGroup = Assert.Single(included);
        Assert.Equal(3, includedGroup.StatementCount);
        Assert.Equal(expected, includedGroup.Occurrences
            .Select(occurrence => occurrence.FilePath + ":" + occurrence.ContainingMember)
            .Order(StringComparer.Ordinal));

        var repeated = await service.RunAsync(solution, new FindDuplicatesOptions(IncludeTests: true));
        Assert.Equal(CaptureJson(included, fixture.RootDir), CaptureJson(repeated, fixture.RootDir));

        var defaultGroups = await service.RunAsync(solution, new FindDuplicatesOptions());
        Assert.DoesNotContain(defaultGroups.SelectMany(group => group.Occurrences),
            occurrence => occurrence.FilePath == sourcePath);
    }

    [Fact]
    public async Task excludes_only_test_framework_context_in_mixed_neutral_named_project()
    {
        const string source = """
                              public class Duplicates
                              {
                              #if NET8_0
                                  public void Net8First()
                              #else
                                  public void Net10First()
                              #endif
                                  {
                                      int x = 1;
                                      int y = 2;
                                      System.Console.WriteLine(x + y);
                                  }

                              #if NET8_0
                                  public void Net8Second()
                              #else
                                  public void Net10Second()
                              #endif
                                  {
                                      int x = 1;
                                      int y = 2;
                                      System.Console.WriteLine(x + y);
                                  }
                              }
                              """;
        using var fixture = new WorkspaceLoaderTestProject();
        var sln = fixture.AddSolution("MixedMetadata", "Metadata", "Catalog", source);
        var projectDirectory = Path.Combine(fixture.RootDir, "MixedMetadata", "Catalog");
        var projectPath = Path.Combine(projectDirectory, "Catalog.csproj");

        var sourcePath = Path.Combine(projectDirectory, "Marker.cs");
        var net8Occurrences = new[] { sourcePath + ":Net8First", sourcePath + ":Net8Second" };
        var net10Occurrences = new[] { sourcePath + ":Net10First", sourcePath + ":Net10Second" };
        var scenarios = new[]
        {
            (TargetFramework: (string?)null, CatalogValidation: "true", OuterIsTestProject: "false"),
            (TargetFramework: "net8.0", CatalogValidation: "true", OuterIsTestProject: "false"),
            (TargetFramework: "net10.0", CatalogValidation: "true", OuterIsTestProject: "false"),
            (TargetFramework: (string?)null, CatalogValidation: "false", OuterIsTestProject: "false"),
            (TargetFramework: (string?)null, CatalogValidation: "true", OuterIsTestProject: "true"),
            (TargetFramework: "net8.0", CatalogValidation: "true", OuterIsTestProject: "true"),
            (TargetFramework: "net10.0", CatalogValidation: "true", OuterIsTestProject: "true")
        };
        MsBuildFixture.Ensure();

        foreach (var scenario in scenarios)
        {
            await File.WriteAllTextAsync(projectPath, $$"""
                                                       <Project Sdk="Microsoft.NET.Sdk">
                                                         <PropertyGroup>
                                                           <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
                                                           <IsTestProject>{{scenario.OuterIsTestProject}}</IsTestProject>
                                                           <IsTestProject Condition="'$(TargetFramework)' == 'net8.0' and '$(CatalogValidation)' == 'true'">false</IsTestProject>
                                                           <IsTestProject Condition="'$(TargetFramework)' == 'net10.0' and '$(CatalogValidation)' == 'true'">true</IsTestProject>
                                                         </PropertyGroup>
                                                       </Project>
                                                       """);

            var properties = new Dictionary<string, string>
            {
                ["CatalogValidation"] = scenario.CatalogValidation
            };
            if (scenario.TargetFramework is not null)
            {
                properties["TargetFramework"] = scenario.TargetFramework;
            }

            using var workspace = Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create(properties);
            var solution = await workspace.OpenSolutionAsync(sln.SolutionPath);
            // Roslyn adds properties such as SolutionDir while opening the solution.
            var loadedProperties = workspace.Properties
                .OrderBy(property => property.Key, StringComparer.Ordinal)
                .ToArray();
            var targetFrameworks = scenario.TargetFramework is null
                ? new[] { "net8.0", "net10.0" }
                : new[] { scenario.TargetFramework };
            Assert.Equal(targetFrameworks.Length, solution.ProjectIds.Count);

            // Check the fixture's metadata independently of duplicate analysis.
            if (scenario.TargetFramework is null)
            {
                using var projects = new Microsoft.Build.Evaluation.ProjectCollection(
                    new Dictionary<string, string>(workspace.Properties));
                var outer = projects.LoadProject(projectPath);
                Assert.Equal(string.Empty, outer.GetPropertyValue("TargetFramework"));
                Assert.Equal(scenario.OuterIsTestProject, outer.GetPropertyValue("IsTestProject"));
            }

            foreach (var targetFramework in targetFrameworks)
            {
                var innerProperties = new Dictionary<string, string>(workspace.Properties)
                {
                    ["TargetFramework"] = targetFramework
                };
                using var projects = new Microsoft.Build.Evaluation.ProjectCollection(innerProperties);
                var inner = projects.LoadProject(projectPath);
                var expectedIsTestProject = targetFramework == "net10.0" && scenario.CatalogValidation == "true";
                Assert.Equal(expectedIsTestProject ? "true" : "false", inner.GetPropertyValue("IsTestProject"));
            }

            foreach (var project in solution.Projects)
            {
                Assert.Equal(projectPath, project.FilePath);
                Assert.StartsWith("Catalog", project.Name);
                var compilation = await project.GetCompilationAsync();
                Assert.NotNull(compilation);
                Assert.False(compilation.ReferencedAssemblyNames.Any(assembly => assembly.Name is
                    "xunit.core" or "xunit.v3.core" or "nunit.framework" or
                    "Microsoft.VisualStudio.TestPlatform.TestFramework"),
                    "The fixture must not reference a recognized test framework.");
            }

            var expectedIncluded = targetFrameworks
                .SelectMany(targetFramework => targetFramework == "net8.0" ? net8Occurrences : net10Occurrences)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var expectedDefault = targetFrameworks
                .Where(targetFramework => targetFramework != "net10.0" || scenario.CatalogValidation != "true")
                .SelectMany(targetFramework => targetFramework == "net8.0" ? net8Occurrences : net10Occurrences)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var service = new FindDuplicatesService();
            var included = await service.RunAsync(solution, new FindDuplicatesOptions(IncludeTests: true));
            var includedGroup = Assert.Single(included);
            Assert.Equal(3, includedGroup.StatementCount);
            Assert.Equal(expectedIncluded, includedGroup.Occurrences
                .Select(occurrence => occurrence.FilePath + ":" + occurrence.ContainingMember)
                .Order(StringComparer.Ordinal)
                .ToArray());

            var repeatedIncluded = await service.RunAsync(solution, new FindDuplicatesOptions(IncludeTests: true));
            Assert.Equal(CaptureJson(included, fixture.RootDir), CaptureJson(repeatedIncluded, fixture.RootDir));

            var defaultGroups = await service.RunAsync(solution, new FindDuplicatesOptions());
            // An unpinned workspace must retain the two net8.0 occurrences, even
            // though the same project file is a test project in its net10.0 context.
            Assert.Equal(expectedDefault, defaultGroups.SelectMany(group => group.Occurrences)
                .Select(occurrence => occurrence.FilePath + ":" + occurrence.ContainingMember)
                .Order(StringComparer.Ordinal)
                .ToArray());
            if (expectedDefault.Length == 0)
            {
                Assert.Empty(defaultGroups);
            }
            else
            {
                Assert.Equal(3, Assert.Single(defaultGroups).StatementCount);
            }

            var repeatedDefault = await service.RunAsync(solution, new FindDuplicatesOptions());
            Assert.Equal(CaptureJson(defaultGroups, fixture.RootDir), CaptureJson(repeatedDefault, fixture.RootDir));
            Assert.Equal(loadedProperties, workspace.Properties
                .OrderBy(property => property.Key, StringComparer.Ordinal)
                .ToArray());
        }
    }

    [Fact]
    public async Task excludes_neutral_named_test_sdk_projects_in_evaluated_framework_context()
    {
        const string source = """
                              public class Duplicates
                              {
                              #if NET8_0
                                  public void Net8First()
                              #else
                                  public void Net10First()
                              #endif
                                  {
                                      int x = 1;
                                      int y = 2;
                                      System.Console.WriteLine(x + y);
                                  }

                              #if NET8_0
                                  public void Net8Second()
                              #else
                                  public void Net10Second()
                              #endif
                                  {
                                      int x = 1;
                                      int y = 2;
                                      System.Console.WriteLine(x + y);
                                  }
                              }
                              """;
        var scenarios = new[]
        {
            (Directory: "DirectSdk", Package: "Microsoft.NET.Test.Sdk", ViaImport: false,
                Multitarget: false, TargetFramework: (string?)null, CatalogValidation: "true", RenameContexts: false),
            (Directory: "ImportedSdk", Package: "mIcRoSoFt.NeT.tEsT.sDk", ViaImport: true,
                Multitarget: false, TargetFramework: (string?)null, CatalogValidation: "true", RenameContexts: false),
            (Directory: "UnrelatedPackage", Package: "System.Text.Json", ViaImport: false,
                Multitarget: false, TargetFramework: (string?)null, CatalogValidation: "true", RenameContexts: false),
            (Directory: "SimilarPackage", Package: "Microsoft.NET.Test.Sdk.Extensions", ViaImport: true,
                Multitarget: false, TargetFramework: (string?)null, CatalogValidation: "true", RenameContexts: false),
            (Directory: "MixedDirectSdk", Package: "Microsoft.NET.Test.Sdk", ViaImport: false,
                Multitarget: true, TargetFramework: (string?)null, CatalogValidation: "true", RenameContexts: false),
            (Directory: "MixedImportedSdk", Package: "mIcRoSoFt.NeT.tEsT.sDk", ViaImport: true,
                Multitarget: true, TargetFramework: (string?)null, CatalogValidation: "true", RenameContexts: false),
            (Directory: "PinnedNet8", Package: "Microsoft.NET.Test.Sdk", ViaImport: true,
                Multitarget: true, TargetFramework: "net8.0", CatalogValidation: "true", RenameContexts: false),
            (Directory: "PinnedNet10", Package: "Microsoft.NET.Test.Sdk", ViaImport: true,
                Multitarget: true, TargetFramework: "net10.0", CatalogValidation: "true", RenameContexts: false),
            (Directory: "DisabledSdk", Package: "Microsoft.NET.Test.Sdk", ViaImport: true,
                Multitarget: true, TargetFramework: (string?)null, CatalogValidation: "false", RenameContexts: false),
            (Directory: "UnmatchedContexts", Package: "Microsoft.NET.Test.Sdk", ViaImport: true,
                Multitarget: true, TargetFramework: (string?)null, CatalogValidation: "true", RenameContexts: true)
        };
        using var fixture = new WorkspaceLoaderTestProject();
        MsBuildFixture.Ensure();

        foreach (var scenario in scenarios)
        {
            var sln = fixture.AddSolution(scenario.Directory, "Packages", "Catalog", source);
            var projectDirectory = Path.Combine(fixture.RootDir, scenario.Directory, "Catalog");
            var projectPath = Path.Combine(projectDirectory, "Catalog.csproj");
            var sourcePath = Path.Combine(projectDirectory, "Marker.cs");
            var frameworkProperty = scenario.Multitarget
                ? "<TargetFrameworks>net8.0;net10.0</TargetFrameworks>"
                : "<TargetFramework>net8.0</TargetFramework>";
            var condition = scenario.Multitarget
                ? " Condition=\"'$(TargetFramework)' == 'net10.0' and '$(CatalogValidation)' == 'true'\""
                : string.Empty;
            // Only evaluated package identity is evidence here: no restore or
            // imported package build assets should supply test metadata/references.
            var packageItems = $"<ItemGroup{condition}><PackageReference Include=\"{scenario.Package}\" Version=\"18.4.0\" /></ItemGroup>";
            if (scenario.ViaImport)
            {
                await File.WriteAllTextAsync(Path.Combine(projectDirectory, "Packages.props"),
                    $"<Project>{packageItems}</Project>");
            }

            await File.WriteAllTextAsync(projectPath, $$"""
                                                       <Project Sdk="Microsoft.NET.Sdk">
                                                         <PropertyGroup>
                                                           {{frameworkProperty}}
                                                           <IsTestProject>false</IsTestProject>
                                                         </PropertyGroup>
                                                         {{(scenario.ViaImport ? "<Import Project=\"Packages.props\" />" : packageItems)}}
                                                       </Project>
                                                       """);
            var properties = new Dictionary<string, string>
            {
                ["CatalogValidation"] = scenario.CatalogValidation
            };
            if (scenario.TargetFramework is not null)
            {
                properties["TargetFramework"] = scenario.TargetFramework;
            }

            using var workspace = Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create(properties);
            var solution = await workspace.OpenSolutionAsync(sln.SolutionPath);
            var loadedProperties = workspace.Properties
                .OrderBy(property => property.Key, StringComparer.Ordinal)
                .ToArray();
            var targetFrameworks = scenario.TargetFramework is not null
                ? new[] { scenario.TargetFramework }
                : scenario.Multitarget ? new[] { "net8.0", "net10.0" } : new[] { "net8.0" };
            Assert.Equal(targetFrameworks.Length, solution.ProjectIds.Count);

            // Independently evaluate the outer and loaded inner contexts rather
            // than using duplicate analysis to establish the fixture's identity.
            if (scenario.Multitarget && scenario.TargetFramework is null)
            {
                using var projects = new Microsoft.Build.Evaluation.ProjectCollection(
                    new Dictionary<string, string>(workspace.Properties));
                var outer = projects.LoadProject(projectPath);
                Assert.Equal(string.Empty, outer.GetPropertyValue("TargetFramework"));
                Assert.Equal("false", outer.GetPropertyValue("IsTestProject"));
                Assert.Empty(outer.GetItems("PackageReference"));
            }

            foreach (var targetFramework in targetFrameworks)
            {
                var innerProperties = new Dictionary<string, string>(workspace.Properties)
                {
                    ["TargetFramework"] = targetFramework
                };
                using var projects = new Microsoft.Build.Evaluation.ProjectCollection(innerProperties);
                var inner = projects.LoadProject(projectPath);
                Assert.Equal(targetFramework, inner.GetPropertyValue("TargetFramework"));
                Assert.Equal("false", inner.GetPropertyValue("IsTestProject"));
                var hasPackage = !scenario.Multitarget
                    || targetFramework == "net10.0" && scenario.CatalogValidation == "true";
                Assert.Equal(hasPackage ? new[] { scenario.Package } : Array.Empty<string>(),
                    inner.GetItems("PackageReference").Select(item => item.EvaluatedInclude).ToArray());
            }

            foreach (var project in solution.Projects)
            {
                Assert.Equal(projectPath, project.FilePath);
                if (scenario.Multitarget && scenario.TargetFramework is null)
                {
                    Assert.True(targetFrameworks.Any(framework => project.Name.EndsWith($"({framework})", StringComparison.OrdinalIgnoreCase)), $"{scenario.Directory}: {project.Name}");
                }
                else if (scenario.Multitarget)
                {
                    Assert.StartsWith("Catalog", project.Name);
                }
                else
                {
                    Assert.Equal("Catalog", project.Name);
                }

                var compilation = await project.GetCompilationAsync();
                Assert.NotNull(compilation);
                Assert.False(compilation.ReferencedAssemblyNames.Any(assembly => assembly.Name is
                    "xunit.core" or "xunit.v3.core" or "nunit.framework" or
                    "Microsoft.VisualStudio.TestPlatform.TestFramework"),
                    "The fixture must not reference a recognized test framework.");
            }

            if (scenario.RenameContexts)
            {
                // A context name that matches no framework must keep outer
                // evaluation; choosing an arbitrary inner context hides code.
                foreach (var project in solution.Projects.ToArray())
                {
                    solution = solution.WithProjectName(project.Id,
                        project.Name.Replace("Catalog (", "Catalog context ", StringComparison.Ordinal)
                            .Replace(")", string.Empty, StringComparison.Ordinal));
                }
            }

            string[] OccurrencesFor(string targetFramework) => targetFramework == "net8.0"
                ? new[] { sourcePath + ":Net8First", sourcePath + ":Net8Second" }
                : new[] { sourcePath + ":Net10First", sourcePath + ":Net10Second" };
            var expectedIncluded = targetFrameworks.SelectMany(OccurrencesFor)
                .Order(StringComparer.Ordinal).ToArray();
            var isTestSdk = scenario.Package is "Microsoft.NET.Test.Sdk" or "mIcRoSoFt.NeT.tEsT.sDk";
            var expectedDefault = targetFrameworks
                .Where(targetFramework => !isTestSdk || scenario.RenameContexts
                    || scenario.Multitarget && (targetFramework != "net10.0" || scenario.CatalogValidation != "true"))
                .SelectMany(OccurrencesFor)
                .Order(StringComparer.Ordinal).ToArray();
            var service = new FindDuplicatesService();
            var included = await service.RunAsync(solution, new FindDuplicatesOptions(IncludeTests: true));
            Assert.Equal(3, Assert.Single(included).StatementCount);
            Assert.Equal(expectedIncluded, included.SelectMany(group => group.Occurrences)
                .Select(occurrence => occurrence.FilePath + ":" + occurrence.ContainingMember)
                .Order(StringComparer.Ordinal).ToArray());
            var repeatedIncluded = await service.RunAsync(solution, new FindDuplicatesOptions(IncludeTests: true));
            Assert.Equal(CaptureJson(included, fixture.RootDir), CaptureJson(repeatedIncluded, fixture.RootDir));

            var defaultGroups = await service.RunAsync(solution, new FindDuplicatesOptions());
            // RED must fail here for DirectSdk: expected no occurrences while
            // the unchanged service still includes both Catalog methods.
            Assert.Equal(expectedDefault, defaultGroups.SelectMany(group => group.Occurrences)
                .Select(occurrence => occurrence.FilePath + ":" + occurrence.ContainingMember)
                .Order(StringComparer.Ordinal).ToArray());
            if (expectedDefault.Length == 0)
            {
                Assert.Empty(defaultGroups);
            }
            else
            {
                Assert.Equal(3, Assert.Single(defaultGroups).StatementCount);
            }

            var repeatedDefault = await service.RunAsync(solution, new FindDuplicatesOptions());
            Assert.Equal(CaptureJson(defaultGroups, fixture.RootDir), CaptureJson(repeatedDefault, fixture.RootDir));
            Assert.Equal(loadedProperties, workspace.Properties
                .OrderBy(property => property.Key, StringComparer.Ordinal).ToArray());
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(MaxStatements: 3));

            groups.ShouldNotContain(g => g.StatementCount > 3);
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            groups.ShouldNotBeEmpty();

            var json = CaptureJson(groups, null);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            root.GetProperty("mode").GetString().ShouldBe("structural");
            var groupsArr = root.GetProperty("groups");
            (groupsArr.GetArrayLength() >= 1).ShouldBeTrue();

            var g = groupsArr[0];
            g.GetProperty("mode").GetString().ShouldBe("structural");
            // extractability is now analysed and must be a known level, not "unknown"
            var ext = g.GetProperty("extractability").GetString()!;
            Assert.True(ext is "extractable" or "likely_extractable" or "discovery_only",
                $"Expected a known extractability level, got: {ext}");
            (g.GetProperty("score").GetDouble() > 0).ShouldBeTrue();
            (g.GetProperty("occurrenceCount").GetInt32() >= 2).ShouldBeTrue();
            (g.GetProperty("preview").GetString()!.Length > 0).ShouldBeTrue();

            var occs = g.GetProperty("occurrences");
            (occs.GetArrayLength() >= 2).ShouldBeTrue();

            var occ = occs[0];
            (occ.GetProperty("file").GetString()!.Length > 0).ShouldBeTrue();
            (occ.GetProperty("line").GetInt32() > 0).ShouldBeTrue();
            (occ.GetProperty("endLine").GetInt32() > 0).ShouldBeTrue();
            (occ.GetProperty("type").GetString()!.Length > 0).ShouldBeTrue();
            (occ.GetProperty("member").GetString()!.Length > 0).ShouldBeTrue();

            var checks = g.GetProperty("checks");
            // checks is now populated with extractability analysis results
            checks.ValueKind.ShouldBe(JsonValueKind.Array);
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            groups.ShouldBeEmpty();

            var json = CaptureJson(groups, null);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            root.GetProperty("mode").GetString().ShouldBe("structural");
            root.GetProperty("groups").GetArrayLength().ShouldBe(0);
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

        var (workspace, sln) = await BuildFixtureAsync(project);
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

            ids2.ShouldBe(ids1);
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            groups.ShouldNotBeEmpty();

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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            groups.ShouldNotBeEmpty();

            var json = CaptureJson(groups, null);
            var doc = JsonDocument.Parse(json);

            var occs = doc.RootElement.GetProperty("groups")[0].GetProperty("occurrences");
            foreach (var occ in occs.EnumerateArray())
            {
                var file = occ.GetProperty("file").GetString()!;
                // Should be just a filename, not an absolute path
                file.ShouldBe(Path.GetFileName(file));
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            groups.ShouldNotBeEmpty();

            var output = CaptureText(groups);
            output.ShouldContain("Group 1", Case.Sensitive);
            output.ShouldContain("preview:", Case.Sensitive);
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            groups.ShouldNotBeEmpty();
            var g = groups.First(g => g.StatementCount == 3);
            g.Extractability.ShouldBe("extractable");
            g.Checks.ShouldNotBeNull();
            (g.Checks.Count > 0).ShouldBeTrue();
            Assert.All(g.Checks, c => c.Status.ShouldBe("pass"));
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(3));

            groups.ShouldNotBeEmpty();
            var g = groups.First(g => g.StatementCount == 3);
            g.Extractability.ShouldBe("discovery_only");
            g.Checks.ShouldNotBeNull();
            var failCheck = g.Checks.FirstOrDefault(c => c.Name == "no-control-flow-escape");
            failCheck.ShouldNotBeNull();
            failCheck.Status.ShouldBe("fail");
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());

            groups.ShouldNotBeEmpty();
            var g = groups.First(g => g.StatementCount >= 3);
            g.Extractability.ShouldBe("discovery_only");
            g.Checks.ShouldNotBeNull();
            var failCheck = g.Checks.FirstOrDefault(c => c.Name == "no-ref-out-params");
            failCheck.ShouldNotBeNull();
            failCheck.Status.ShouldBe("fail");
        }
    }

    [Fact]
    public async Task
        handles_group_with_differing_captured_fields_is_likely_extractable_with_consistent_captured_state_warn()
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

        var (workspace, sln) = await BuildFixtureAsync(builder);
        using (workspace)
        {
            // "CaptureTest" ends with "Test" so it would be excluded as a test project;
            // use IncludeTests:true so the project is scanned.
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(IncludeTests: true));

            groups.ShouldNotBeEmpty();
            var g = groups.First(g => g.StatementCount >= 3);
            // consistent-captured-state should warn (A._count vs B._count are different symbols)
            g.Checks.ShouldNotBeNull();
            var captureCheck = g.Checks.FirstOrDefault(c => c.Name == "consistent-captured-state");
            captureCheck.ShouldNotBeNull();
            captureCheck.Status.ShouldBe("warn");
            // Overall: at least likely_extractable (warn present, not extractable)
            g.Extractability.ShouldNotBe("extractable");
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            groups.ShouldNotBeEmpty();

            var json = CaptureJson(groups, null);
            var doc = JsonDocument.Parse(json);
            var g = doc.RootElement.GetProperty("groups")[0];

            // extractability must be a non-empty string (extractable/likely_extractable/discovery_only)
            var ext = g.GetProperty("extractability").GetString()!;
            Assert.True(ext.Length > 0 && ext != "unknown", $"Expected known extractability, got: {ext}");

            // checks must be an array (may be empty for extractable groups but structure must be present)
            var checks = g.GetProperty("checks");
            checks.ValueKind.ShouldBe(JsonValueKind.Array);

            // Each check entry (if any) must have name, status, and detail fields
            foreach (var check in checks.EnumerateArray())
            {
                check.TryGetProperty("name", out _).ShouldBeTrue();
                check.TryGetProperty("status", out _).ShouldBeTrue();
                check.TryGetProperty("detail", out _).ShouldBeTrue();
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            groups.ShouldNotBeEmpty();

            var output = CaptureText(groups);
            output.ShouldContain("extractable=", Case.Sensitive);
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            groups.ShouldNotBeEmpty();

            var output = CaptureText(groups);
            var occurrenceLines = output.Split('\n').Where(l =>
                l.TrimStart().StartsWith("1.", StringComparison.Ordinal) ||
                l.TrimStart().StartsWith("2.", StringComparison.Ordinal));
            Assert.All(occurrenceLines, line => line.ShouldNotContain('\\'));
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            groups.ShouldNotBeEmpty();

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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            groups.ShouldNotBeEmpty();

            Assert.All(groups.SelectMany(g => g.Occurrences), o => (o.EndLine >= o.StartLine).ShouldBeTrue());
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            groups.ShouldNotBeEmpty();

            var json = JsonDocument.Parse(CaptureJson(groups, null));
            var occurrences = json.RootElement.GetProperty("groups")[0].GetProperty("occurrences");
            foreach (var occ in occurrences.EnumerateArray())
            {
                var link = occ.GetProperty("link").GetString()!;
                link.ShouldNotContain('\\');
                Assert.Matches(@":\d+$", link);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static async Task<(Workspace Workspace, Solution Solution)> BuildFixtureAsync(AdhocProjectBuilder builder)
    {
        var (workspace, solution) = builder.Build();
        // The builder copies all test-host TPA references, including xUnit. Remove only
        // incidental primary test frameworks here so these fixtures model production.
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            Assert.NotNull(compilation);
            var references = project.MetadataReferences.Where(reference =>
                compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol
                {
                    Identity.Name: "xunit.core" or "xunit.v3.core" or "nunit.framework"
                        or "Microsoft.VisualStudio.TestPlatform.TestFramework"
                });
            solution = solution.WithProjectMetadataReferences(project.Id, references);
        }

        return (workspace, solution);
    }

    private static PortableExecutableReference CreateAssemblyReference(string assemblyName, string filePath)
    {
        var compilation = CSharpCompilation.Create(assemblyName,
            [CSharpSyntaxTree.ParseText("public class ReferenceMarker { }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return MetadataReference.CreateFromImage(image.ToArray(), filePath: filePath);
    }

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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(1));

            // The single guard clause `if (IsNullOrWhiteSpace...) return false;` is a
            // 1-statement duplicate group. It must surface as likely_extractable.
            var guardGroup = groups.FirstOrDefault(g =>
                g.FullBlock.Contains("IsNullOrWhiteSpace") &&
                (g.Extractability == "likely_extractable" || g.Extractability == "extractable"));

            guardGroup.ShouldNotBeNull();

            // The no-control-flow-escape check must be warn for ExtractAsBoolGuard
            var cfCheck = guardGroup.Checks?.FirstOrDefault(c => c.Name == "no-control-flow-escape");
            cfCheck.ShouldNotBeNull();
            cfCheck!.Status.ShouldBe("warn");
            Assert.Contains("guard pattern", cfCheck.Detail, StringComparison.OrdinalIgnoreCase);

            // Overall extractability must not be discovery_only
            guardGroup.Extractability.ShouldNotBe("discovery_only");
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

        var (workspace, sln) = await BuildFixtureAsync(project);
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions(2));

            // The 2-statement compute block (var lo / var hi) is the only duplicate;
            // the callers differ so no larger window is a duplicate, so the 2-stmt group
            // survives suppression.
            groups.ShouldNotBeEmpty();
            var multiOutputGroup = groups[0];

            // Verify this is indeed the compute block
            multiOutputGroup.StatementCount.ShouldBe(2);

            // The single-output check must be warn (tuple-extractable), not fail
            var soCheck = multiOutputGroup.Checks?.FirstOrDefault(c => c.Name == "single-output");
            soCheck.ShouldNotBeNull();
            soCheck!.Status.ShouldBe("warn");

            // Overall extractability must not be discovery_only
            multiOutputGroup.Extractability.ShouldNotBe("discovery_only");
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
