using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Commands.Results;
using RoslynTools.Core.Output;

namespace RoslynTools.Tests.Analysis;

public class ListSmellsMigration : IDisposable
{
    public ListSmellsMigration()
    {
        RtContext.Format = OutputFormat.Text;
    }

    public void Dispose()
    {
        RtContext.Format = OutputFormat.Text;
        GC.SuppressFinalize(this);
    }

    private static ListSmellsResult BuildResult(
        params (string SmellType, string TypeName, string? MemberName, string FilePath, int Line, string Detail)[]
            items)
    {
        return new ListSmellsResult
        {
            Smells = items.Select(i => new SmellItemResult
            {
                SmellType = i.SmellType,
                TypeName = i.TypeName,
                MemberName = i.MemberName,
                FilePath = i.FilePath,
                Line = i.Line,
                Detail = i.Detail
            }).ToList()
        };
    }

    // ── AC-3: RenderText reproduces legacy columnar format byte-for-byte ──────────

    [Fact]
    public void renders_text_matching_legacy_columnar_format_with_member()
    {
        var result = BuildResult(("LargeClass", "MyBigClass", "DoWork", "/src/MyBigClass.cs", 42, "310 lines"));

        var sw = new StringWriter();
        result.RenderText(sw);
        var actual = sw.ToString();

        var member = "  DoWork";
        var expected = $"{"LargeClass",-24}{"MyBigClass",-30}{member,-20}/src/MyBigClass.cs:42  310 lines";
        Assert.Contains(expected, actual);
    }

    [Fact]
    public void renders_text_matching_legacy_columnar_format_without_member()
    {
        var result = BuildResult(("LargeClass", "MyBigClass", null, "/src/MyBigClass.cs", 42, "310 lines"));

        var sw = new StringWriter();
        result.RenderText(sw);
        var actual = sw.ToString();

        var member = "";
        var expected = $"{"LargeClass",-24}{"MyBigClass",-30}{member,-20}/src/MyBigClass.cs:42  310 lines";
        Assert.Contains(expected, actual);
    }

    [Fact]
    public void renders_text_with_one_line_per_smell()
    {
        var result = BuildResult(
            ("LargeClass", "Alpha", null, "/a.cs", 1, "detail1"),
            ("LongMethod", "Beta", "Run", "/b.cs", 5, "detail2"));

        var sw = new StringWriter();
        result.RenderText(sw);
        var lines = sw.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
    }

    // ── AC-4: --json produces valid JSON with all smells and their details ─────────

    [Fact]
    public void emits_json_format_produces_valid_json_with_all_smell_fields()
    {
        var result = BuildResult(
            ("LargeClass", "BigClass", null, "/src/Big.cs", 10, "350 lines"),
            ("LongMethod", "SomeService", "Execute", "/src/SomeService.cs", 55, "60 lines"));

        var sw = new StringWriter();
        RtContext.Format = OutputFormat.Json;
        RtSerializer.Write(result, sw);

        var doc = JsonDocument.Parse(sw.ToString());
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("smells", out var smells));
        Assert.Equal(JsonValueKind.Array, smells.ValueKind);
        Assert.Equal(2, smells.GetArrayLength());

        var first = smells[0];
        Assert.True(first.TryGetProperty("smellType", out _));
        Assert.True(first.TryGetProperty("typeName", out _));
        Assert.True(first.TryGetProperty("filePath", out _));
        Assert.True(first.TryGetProperty("line", out _));
        Assert.True(first.TryGetProperty("detail", out _));
    }

    // ── AC-5: --markdown produces grouped Markdown output ─────────────────────────

    [Fact]
    public void emits_markdown_format_produces_grouped_output_with_h1_and_h2_per_smell_type()
    {
        var result = BuildResult(
            ("LargeClass", "BigClass", null, "/src/Big.cs", 10, "350 lines"),
            ("LongMethod", "SomeService", "Execute", "/src/SomeService.cs", 55, "60 lines"));

        var sw = new StringWriter();
        RtContext.Format = OutputFormat.Markdown;
        RtSerializer.Write(result, sw);
        var md = sw.ToString();

        Assert.Contains("# Code Smells", md);
        Assert.Contains("## LargeClass", md);
        Assert.Contains("## LongMethod", md);
        Assert.Contains("BigClass", md);
        Assert.Contains("SomeService", md);
    }

    [Fact]
    public void emits_markdown_format_empty_smells_prints_no_smells_found()
    {
        var result = new ListSmellsResult { Smells = [] };

        var sw = new StringWriter();
        RtContext.Format = OutputFormat.Markdown;
        RtSerializer.Write(result, sw);
        var md = sw.ToString();

        Assert.Contains("No code smells found.", md);
    }

    // ── AC-6: --llm produces compact per-smell lines with only Llm-level fields ───

    [Fact]
    public void emits_llm_format_produces_compact_output_with_smell_type_and_type_name()
    {
        var result = BuildResult(
            ("LargeClass", "BigClass", null, "/src/Big.cs", 10, "350 lines"));

        var sw = new StringWriter();
        RtContext.Format = OutputFormat.Llm;
        RtSerializer.Write(result, sw);
        var output = sw.ToString();

        Assert.Contains("LargeClass", output);
        Assert.Contains("BigClass", output);
        // Default-level fields should not appear as labelled properties
        Assert.DoesNotContain("filePath:", output);
        Assert.DoesNotContain("line:", output);
        Assert.DoesNotContain("detail:", output);
    }

    // ── AC-7: no flag produces unchanged legacy text output ───────────────────────

    [Fact]
    public void uses_text_format_as_default_matching_legacy_columnar_output()
    {
        var result = BuildResult(("TooManyDependencies", "FatService", null, "/src/Fat.cs", 3, "8 params"));

        var sw = new StringWriter();
        RtContext.Format = OutputFormat.Text;
        RtSerializer.Write(result, sw);
        var output = sw.ToString();

        var member = "";
        var expected = $"{"TooManyDependencies",-24}{"FatService",-30}{member,-20}/src/Fat.cs:3  8 params";
        Assert.Contains(expected, output);
    }

    // ── AC-8: RtCommandInfo.SupportedFormats includes all four formats ─────────────

    [Fact]
    public void list_smells_command_info_has_all_four_supported_formats()
    {
        var info = ListSmellsCommand.Info;
        var formats = info.SupportedFormats;
        Assert.NotNull(formats);
        Assert.Contains(OutputFormat.Text, formats);
        Assert.Contains(OutputFormat.Json, formats);
        Assert.Contains(OutputFormat.Markdown, formats);
        Assert.Contains(OutputFormat.Llm, formats);
    }
}
