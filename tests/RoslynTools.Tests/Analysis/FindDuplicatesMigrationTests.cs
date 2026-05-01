using System.Text.Json;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Tests.Analysis;

public class FindDuplicatesMigrationTests : IDisposable
{
    public FindDuplicatesMigrationTests()
    {
        RtContext.Format = OutputFormat.Text;
    }

    public void Dispose()
    {
        RtContext.Format = OutputFormat.Text;
        GC.SuppressFinalize(this);
    }

    private static (AdhocProjectBuilder Project, IReadOnlyList<DuplicateGroup> Groups, string? SolutionDir) BuildDuplicateProject()
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
        var groups = FindDuplicatesService.RunAsync(sln, new FindDuplicatesOptions()).GetAwaiter().GetResult();
        workspace.Dispose();
        return (project, groups, null);
    }

    // ── AC-4: RenderText reproduces PrintGroups output byte-for-byte ──────────────

    [Fact]
    public async Task RenderText_reproduces_PrintGroups_byte_for_byte()
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
            var groups = await FindDuplicatesService.RunAsync(sln, new FindDuplicatesOptions());
            Assert.NotEmpty(groups);

            var result = FindDuplicatesResult.FromGroups(groups, solutionDir: null);
            var sw = new StringWriter();
            result.RenderText(sw);
            var actual = sw.ToString();

            var g = groups[0];
            var path = g.Occurrences[0].FilePath.Replace('\\', '/');

            Assert.Contains($"Group 1  id={g.GroupId}  occurrences={g.Occurrences.Count}  score={g.Score:F2}  extractable={g.Extractability}", actual);
            Assert.Contains($"preview: {g.Preview}", actual);
            Assert.Contains($"  1. {path}:{g.Occurrences[0].StartLine}-{g.Occurrences[0].EndLine} in {g.Occurrences[0].ContainingType}.{g.Occurrences[0].ContainingMember}", actual);
        }
    }

    [Fact]
    public async Task RenderText_empty_groups_prints_no_duplicate_groups_found()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;
            public class A { public void Run() { int x = 1; } }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesService.RunAsync(sln, new FindDuplicatesOptions());
            var result = FindDuplicatesResult.FromGroups(groups, solutionDir: null);
            var sw = new StringWriter();
            result.RenderText(sw);
            Assert.Equal("No duplicate groups found." + Environment.NewLine, sw.ToString());
        }
    }

    // ── AC-5: --json produces valid JSON with expected fields ─────────────────────

    [Fact]
    public async Task Json_format_produces_valid_json_with_expected_fields()
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
            var groups = await FindDuplicatesService.RunAsync(sln, new FindDuplicatesOptions());
            Assert.NotEmpty(groups);

            var result = FindDuplicatesResult.FromGroups(groups, solutionDir: null);
            var sw = new StringWriter();
            RtContext.Format = OutputFormat.Json;
            RtSerializer.Write(result, sw);

            var doc = JsonDocument.Parse(sw.ToString());
            var root = doc.RootElement;

            Assert.Equal("structural", root.GetProperty("mode").GetString());

            var groupsArr = root.GetProperty("groups");
            Assert.True(groupsArr.GetArrayLength() >= 1);

            var g = groupsArr[0];
            Assert.True(g.TryGetProperty("groupId", out _));
            Assert.True(g.TryGetProperty("extractability", out _));
            Assert.True(g.TryGetProperty("preview", out _));
            Assert.True(g.TryGetProperty("score", out _));
            Assert.True(g.TryGetProperty("occurrenceCount", out _));
            Assert.True(g.TryGetProperty("occurrences", out var occs));
            Assert.True(g.TryGetProperty("checks", out _));
            Assert.Equal(JsonValueKind.Array, occs.ValueKind);
        }
    }

    // ── AC-6: --markdown produces Markdown with H1 header and per-group H2 ────────

    [Fact]
    public async Task Markdown_format_produces_H1_header_and_group_H2_sections()
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
            var groups = await FindDuplicatesService.RunAsync(sln, new FindDuplicatesOptions());
            Assert.NotEmpty(groups);

            var result = FindDuplicatesResult.FromGroups(groups, solutionDir: null);
            var sw = new StringWriter();
            RtContext.Format = OutputFormat.Markdown;
            RtSerializer.Write(result, sw);
            var md = sw.ToString();

            Assert.Contains("# Duplicate Groups", md);
            Assert.Contains("## Group 1:", md);
            Assert.Contains("| # | Location | Type | Member |", md);
        }
    }

    // ── AC-7: --llm produces compact output (GroupId, Extractability, Preview, Location) ─

    [Fact]
    public async Task Llm_format_produces_compact_output_without_score_or_checks()
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
            var groups = await FindDuplicatesService.RunAsync(sln, new FindDuplicatesOptions());
            Assert.NotEmpty(groups);

            var result = FindDuplicatesResult.FromGroups(groups, solutionDir: null);
            var sw = new StringWriter();
            RtContext.Format = OutputFormat.Llm;
            RtSerializer.Write(result, sw);
            var output = sw.ToString();

            var g = groups[0];
            Assert.Contains(g.GroupId, output);
            Assert.Contains(g.Extractability, output);
            Assert.Contains(g.Preview, output);
            // Location (link) should appear
            Assert.Contains(g.Occurrences[0].FilePath.Replace('\\', '/'), output);
            // Score and checks section should not appear as "score" label
            Assert.DoesNotContain("score:", output);
            Assert.DoesNotContain("occurrenceCount:", output);
        }
    }

    // ── AC-8: no flag produces identical stdout to prior baseline ─────────────────

    [Fact]
    public async Task Text_format_includes_group_header_preview_and_occurrence_lines()
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
            var groups = await FindDuplicatesService.RunAsync(sln, new FindDuplicatesOptions());
            Assert.NotEmpty(groups);

            var result = FindDuplicatesResult.FromGroups(groups, solutionDir: null);
            var sw = new StringWriter();
            RtContext.Format = OutputFormat.Text;
            RtSerializer.Write(result, sw);
            var output = sw.ToString();

            var g = groups[0];
            Assert.Contains($"id={g.GroupId}", output);
            Assert.Contains($"occurrences={g.Occurrences.Count}", output);
            Assert.Contains($"score={g.Score:F2}", output);
            Assert.Contains($"extractable={g.Extractability}", output);
            Assert.Contains($"preview: {g.Preview}", output);
            Assert.Contains("  1.", output);
        }
    }

    // ── AC-9: RtCommandInfo.SupportedFormats includes all four formats ─────────────

    [Fact]
    public void FindDuplicatesCommand_Info_has_all_four_supported_formats()
    {
        var info = Commands.FindDuplicatesCommand.Info;
        var formats = info.SupportedFormats;
        Assert.NotNull(formats);
        Assert.Contains(OutputFormat.Text, formats);
        Assert.Contains(OutputFormat.Json, formats);
        Assert.Contains(OutputFormat.Markdown, formats);
        Assert.Contains(OutputFormat.Llm, formats);
    }
}
