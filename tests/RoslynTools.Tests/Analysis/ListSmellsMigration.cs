using System.CommandLine;
using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Services;
using RoslynTools.Core.Output;
using Shouldly;

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
        StaticAnalyzerResultAdapters.ListSmells(result).RenderText(sw);
        var actual = sw.ToString();

        var member = "  DoWork";
        var expected = $"{"LargeClass",-24}{"MyBigClass",-30}{member,-20}/src/MyBigClass.cs:42  310 lines";
        actual.ShouldContain(expected, Case.Sensitive);
    }

    [Fact]
    public void renders_text_matching_legacy_columnar_format_without_member()
    {
        var result = BuildResult(("LargeClass", "MyBigClass", null, "/src/MyBigClass.cs", 42, "310 lines"));

        var sw = new StringWriter();
        StaticAnalyzerResultAdapters.ListSmells(result).RenderText(sw);
        var actual = sw.ToString();

        var member = "";
        var expected = $"{"LargeClass",-24}{"MyBigClass",-30}{member,-20}/src/MyBigClass.cs:42  310 lines";
        actual.ShouldContain(expected, Case.Sensitive);
    }

    [Fact]
    public void renders_text_with_one_line_per_smell()
    {
        var result = BuildResult(
            ("LargeClass", "Alpha", null, "/a.cs", 1, "detail1"),
            ("LongMethod", "Beta", "Run", "/b.cs", 5, "detail2"));

        var sw = new StringWriter();
        StaticAnalyzerResultAdapters.ListSmells(result).RenderText(sw);
        var lines = sw.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(2);
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
        RtSerializer.Write(StaticAnalyzerResultAdapters.ListSmells(result), sw);

        var doc = JsonDocument.Parse(sw.ToString());
        var root = doc.RootElement;

        root.TryGetProperty("smells", out var smells).ShouldBeTrue();
        smells.ValueKind.ShouldBe(JsonValueKind.Array);
        smells.GetArrayLength().ShouldBe(2);

        var first = smells[0];
        first.TryGetProperty("smellType", out _).ShouldBeTrue();
        first.TryGetProperty("typeName", out _).ShouldBeTrue();
        first.TryGetProperty("filePath", out _).ShouldBeTrue();
        first.TryGetProperty("line", out _).ShouldBeTrue();
        first.TryGetProperty("detail", out _).ShouldBeTrue();
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
        RtSerializer.Write(StaticAnalyzerResultAdapters.ListSmells(result), sw);
        var md = sw.ToString();

        md.ShouldContain("# Code Smells", Case.Sensitive);
        md.ShouldContain("## LargeClass", Case.Sensitive);
        md.ShouldContain("## LongMethod", Case.Sensitive);
        md.ShouldContain("BigClass", Case.Sensitive);
        md.ShouldContain("SomeService", Case.Sensitive);
    }

    [Fact]
    public void emits_markdown_format_empty_smells_prints_no_smells_found()
    {
        var result = new ListSmellsResult { Smells = [] };

        var sw = new StringWriter();
        RtContext.Format = OutputFormat.Markdown;
        RtSerializer.Write(StaticAnalyzerResultAdapters.ListSmells(result), sw);
        var md = sw.ToString();

        md.ShouldContain("No code smells found.", Case.Sensitive);
    }

    // ── AC-6: --llm produces compact per-smell lines with only Llm-level fields ───

    [Fact]
    public void emits_llm_format_produces_compact_output_with_smell_type_and_type_name()
    {
        var result = BuildResult(
            ("LargeClass", "BigClass", null, "/src/Big.cs", 10, "350 lines"));

        var sw = new StringWriter();
        RtContext.Format = OutputFormat.Llm;
        RtSerializer.Write(StaticAnalyzerResultAdapters.ListSmells(result), sw);
        var output = sw.ToString();

        output.ShouldContain("LargeClass", Case.Sensitive);
        output.ShouldContain("BigClass", Case.Sensitive);
        // Default-level fields should not appear as labelled properties
        output.ShouldNotContain("filePath:", Case.Sensitive);
        output.ShouldNotContain("line:", Case.Sensitive);
        output.ShouldNotContain("detail:", Case.Sensitive);
    }

    // ── AC-7: no flag produces unchanged legacy text output ───────────────────────

    [Fact]
    public void uses_text_format_as_default_matching_legacy_columnar_output()
    {
        var result = BuildResult(("TooManyDependencies", "FatService", null, "/src/Fat.cs", 3, "8 params"));

        var sw = new StringWriter();
        RtContext.Format = OutputFormat.Text;
        RtSerializer.Write(StaticAnalyzerResultAdapters.ListSmells(result), sw);
        var output = sw.ToString();

        var member = "";
        var expected = $"{"TooManyDependencies",-24}{"FatService",-30}{member,-20}/src/Fat.cs:3  8 params";
        output.ShouldContain(expected, Case.Sensitive);
    }

    // ── AC-8: RtCommandInfo.SupportedFormats includes all four formats ─────────────

    [Fact]
    public void list_smells_command_info_has_all_four_supported_formats()
    {
        var info = ListSmellsCommand.Info;
        var formats = info.SupportedFormats;
        formats.ShouldNotBeNull();
        formats.ShouldContain(OutputFormat.Text);
        formats.ShouldContain(OutputFormat.Json);
        formats.ShouldContain(OutputFormat.Markdown);
        formats.ShouldContain(OutputFormat.Llm);
    }

    [Fact]
    public void list_smells_command_accepts_top_option()
    {
        var command = App.Commands.Single(entry => entry.Info.Name == "list-smells").Build();

        var result = command.Parse("--solution RoslynTools.slnx --top 3");

        result.Errors.ShouldBeEmpty();
    }
}
