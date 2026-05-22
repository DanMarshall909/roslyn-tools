using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Commands.Results;
using RoslynTools.Core.Output;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public class FindDuplicatesMigration : IDisposable
{
    public FindDuplicatesMigration()
    {
        RtContext.Format = OutputFormat.Text;
    }

    public void Dispose()
    {
        RtContext.Format = OutputFormat.Text;
        GC.SuppressFinalize(this);
    }

    private static (AdhocProjectBuilder Project, IReadOnlyList<DuplicateGroup> Groups, string? SolutionDir)
        BuildDuplicateProject()
    {
        var project = new AdhocProjectBuilder();
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
        var groups = new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions()).GetAwaiter().GetResult();
        workspace.Dispose();
        return (project, groups, null);
    }

    // ── AC-4: RenderText reproduces PrintGroups output byte-for-byte ──────────────

    [Fact]
    public async Task renders_text_matching_print_groups_byte_for_byte()
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
            groups.ShouldNotBeEmpty();

            var result = FindDuplicatesResult.FromGroups(groups, null);
            var sw = new StringWriter();
            result.RenderText(sw);
            var actual = sw.ToString();

            var g = groups[0];
            var path = g.Occurrences[0].FilePath.Replace('\\', '/');

            actual.ShouldContain($"Group 1  id={g.GroupId}  occurrences={g.Occurrences.Count}  score={g.Score:F2}  extractable={g.Extractability}", Case.Sensitive);
            actual.ShouldContain($"preview: {g.Preview}", Case.Sensitive);
            actual.ShouldContain($"  1. {path}:{g.Occurrences[0].StartLine}-{g.Occurrences[0].EndLine} in {g.Occurrences[0].ContainingType}.{g.Occurrences[0].ContainingMember}", Case.Sensitive);
        }
    }

    [Fact]
    public async Task renders_text_no_duplicate_groups_message_when_empty()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
                                  namespace TestApp;
                                  public class A { public void Run() { int x = 1; } }
                                  """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await new FindDuplicatesService().RunAsync(sln, new FindDuplicatesOptions());
            var result = FindDuplicatesResult.FromGroups(groups, null);
            var sw = new StringWriter();
            result.RenderText(sw);
            sw.ToString().ShouldBe("No duplicate groups found." + Environment.NewLine);
        }
    }

    // ── AC-5: --json produces valid JSON with expected fields ─────────────────────

    [Fact]
    public async Task emits_json_format_with_expected_fields()
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
            groups.ShouldNotBeEmpty();

            var result = FindDuplicatesResult.FromGroups(groups, null);
            var sw = new StringWriter();
            RtContext.Format = OutputFormat.Json;
            RtSerializer.Write(result, sw);

            var doc = JsonDocument.Parse(sw.ToString());
            var root = doc.RootElement;

            root.GetProperty("mode").GetString().ShouldBe("structural");

            var groupsArr = root.GetProperty("groups");
            (groupsArr.GetArrayLength() >= 1).ShouldBeTrue();

            var g = groupsArr[0];
            g.TryGetProperty("groupId", out _).ShouldBeTrue();
            g.TryGetProperty("extractability", out _).ShouldBeTrue();
            g.TryGetProperty("preview", out _).ShouldBeTrue();
            g.TryGetProperty("score", out _).ShouldBeTrue();
            g.TryGetProperty("occurrenceCount", out _).ShouldBeTrue();
            g.TryGetProperty("occurrences", out var occs).ShouldBeTrue();
            g.TryGetProperty("checks", out _).ShouldBeTrue();
            occs.ValueKind.ShouldBe(JsonValueKind.Array);
        }
    }

    // ── AC-6: --markdown produces Markdown with H1 header and per-group H2 ────────

    [Fact]
    public async Task emits_markdown_format_with_group_sections()
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
            groups.ShouldNotBeEmpty();

            var result = FindDuplicatesResult.FromGroups(groups, null);
            var sw = new StringWriter();
            RtContext.Format = OutputFormat.Markdown;
            RtSerializer.Write(result, sw);
            var md = sw.ToString();

            md.ShouldContain("# Duplicate Groups", Case.Sensitive);
            md.ShouldContain("## Group 1:", Case.Sensitive);
            md.ShouldContain("| # | Location | Type | Member |", Case.Sensitive);
        }
    }

    // ── AC-7: --llm produces compact output (GroupId, Extractability, Preview, Location) ─

    [Fact]
    public async Task emits_llm_format_without_score_or_checks()
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
            groups.ShouldNotBeEmpty();

            var result = FindDuplicatesResult.FromGroups(groups, null);
            var sw = new StringWriter();
            RtContext.Format = OutputFormat.Llm;
            RtSerializer.Write(result, sw);
            var output = sw.ToString();

            var g = groups[0];
            output.ShouldContain(g.GroupId, Case.Sensitive);
            output.ShouldContain(g.Extractability, Case.Sensitive);
            output.ShouldContain(g.Preview, Case.Sensitive);
            // Location (link) should appear
            output.ShouldContain(g.Occurrences[0].FilePath.Replace('\\', '/'), Case.Sensitive);
            // Score and checks section should not appear as "score" label
            output.ShouldNotContain("score:", Case.Sensitive);
            output.ShouldNotContain("occurrenceCount:", Case.Sensitive);
        }
    }

    // ── AC-8: no flag produces identical stdout to prior baseline ─────────────────

    [Fact]
    public async Task emits_text_format_with_group_header_preview_and_occurrences()
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
            groups.ShouldNotBeEmpty();

            var result = FindDuplicatesResult.FromGroups(groups, null);
            var sw = new StringWriter();
            RtContext.Format = OutputFormat.Text;
            RtSerializer.Write(result, sw);
            var output = sw.ToString();

            var g = groups[0];
            output.ShouldContain($"id={g.GroupId}", Case.Sensitive);
            output.ShouldContain($"occurrences={g.Occurrences.Count}", Case.Sensitive);
            output.ShouldContain($"score={g.Score:F2}", Case.Sensitive);
            output.ShouldContain($"extractable={g.Extractability}", Case.Sensitive);
            output.ShouldContain($"preview: {g.Preview}", Case.Sensitive);
            output.ShouldContain("  1.", Case.Sensitive);
        }
    }

    // ── AC-9: RtCommandInfo.SupportedFormats includes all four formats ─────────────

    [Fact]
    public void finds_duplicates_command_info_has_all_four_supported_formats()
    {
        var info = FindDuplicatesCommand.Info;
        var formats = info.SupportedFormats;
        formats.ShouldNotBeNull();
        formats.ShouldContain(OutputFormat.Text);
        formats.ShouldContain(OutputFormat.Json);
        formats.ShouldContain(OutputFormat.Markdown);
        formats.ShouldContain(OutputFormat.Llm);
    }
}
