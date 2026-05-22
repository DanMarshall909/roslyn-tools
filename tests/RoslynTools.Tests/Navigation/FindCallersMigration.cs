using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Core.Output;
using RoslynTools.Services.Results;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class FindCallersMigration : IDisposable
{
    public FindCallersMigration()
    {
        RtContext.Format = OutputFormat.Text;
        RtContext.Level = OutputLevel.Default;
    }

    public void Dispose()
    {
        RtContext.Format = OutputFormat.Text;
        RtContext.Level = OutputLevel.Default;
    }

    // ── RenderText snapshot ───────────────────────────────────────────────────────

    [Fact]
    public void renders_text_matching_legacy_format_for_each_caller()
    {
        var result = MakeFindCallersResult();
        using var sw = new StringWriter();

        result.RenderText(sw);

        var output = sw.ToString();
        output.ShouldContain("/src/Foo.cs:10: RunJob: DoWork();", Case.Sensitive);
        output.ShouldContain("/src/Bar.cs:42: Execute: DoWork();", Case.Sensitive);
    }

    [Fact]
    public void renders_text_with_newline_after_each_line()
    {
        var result = MakeFindCallersResult();
        using var sw = new StringWriter();

        result.RenderText(sw);

        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(2);
    }

    // ── CallerResult.RenderText byte-for-byte ─────────────────────────────────────

    [Fact]
    public void caller_result_render_text_produces_exact_format()
    {
        var caller = new CallerResult
        {
            FilePath = "C:/code/Foo.cs",
            Line = 7,
            ContainingMethod = "MyMethod",
            SourceLine = "foo.Bar();"
        };
        using var sw = new StringWriter();

        caller.RenderText(sw);

        sw.ToString().ShouldBe("C:/code/Foo.cs:7: MyMethod: foo.Bar();");
    }

    // ── JSON format ───────────────────────────────────────────────────────────────

    [Fact]
    public void emits_json_output_contains_symbol_name_and_callers_array()
    {
        RtContext.Format = OutputFormat.Json;
        var result = MakeFindCallersResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var doc = JsonDocument.Parse(sw.ToString());
        var root = doc.RootElement;

        root.TryGetProperty("symbolName", out var sym).ShouldBeTrue();
        sym.GetString().ShouldBe("DoWork");

        root.TryGetProperty("callers", out var callers).ShouldBeTrue();
        callers.ValueKind.ShouldBe(JsonValueKind.Array);
        callers.GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public void emits_json_caller_entries_contain_expected_fields()
    {
        RtContext.Format = OutputFormat.Json;
        var result = MakeFindCallersResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var doc = JsonDocument.Parse(sw.ToString());
        var first = doc.RootElement.GetProperty("callers")[0];

        first.TryGetProperty("filePath", out _).ShouldBeTrue();
        first.TryGetProperty("line", out _).ShouldBeTrue();
        first.TryGetProperty("containingMethod", out _).ShouldBeTrue();
        first.TryGetProperty("sourceLine", out _).ShouldBeTrue();
    }

    // ── Markdown format ───────────────────────────────────────────────────────────

    [Fact]
    public void emits_markdown_output_contains_caller_data()
    {
        RtContext.Format = OutputFormat.Markdown;
        var result = MakeFindCallersResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var md = sw.ToString();
        md.ShouldContain("RunJob", Case.Sensitive);
        md.ShouldContain("Execute", Case.Sensitive);
    }

    // ── LLM format ───────────────────────────────────────────────────────────────

    [Fact]
    public void emits_llm_output_contains_symbol_name()
    {
        RtContext.Format = OutputFormat.Llm;
        var result = MakeFindCallersResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var llm = sw.ToString();
        llm.ShouldContain("DoWork", Case.Sensitive);
    }

    [Fact]
    public void emits_llm_output_contains_caller_file_paths()
    {
        RtContext.Format = OutputFormat.Llm;
        var result = MakeFindCallersResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var llm = sw.ToString();
        llm.ShouldContain("/src/Foo.cs", Case.Sensitive);
        llm.ShouldContain("/src/Bar.cs", Case.Sensitive);
    }

    // ── SupportedFormats ─────────────────────────────────────────────────────────

    [Fact]
    public void finds_callers_command_info_supported_formats_contains_all_four_formats()
    {
        var formats = FindCallersCommand.Info.SupportedFormats;
        formats.ShouldNotBeNull();
        formats.ShouldContain(OutputFormat.Text);
        formats.ShouldContain(OutputFormat.Json);
        formats.ShouldContain(OutputFormat.Markdown);
        formats.ShouldContain(OutputFormat.Llm);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static FindCallersResult MakeFindCallersResult()
    {
        return new FindCallersResult
        {
            SymbolName = "DoWork",
            Callers =
            [
                new CallerResult
                {
                    FilePath = "/src/Foo.cs",
                    Line = 10,
                    ContainingMethod = "RunJob",
                    SourceLine = "DoWork();"
                },
                new CallerResult
                {
                    FilePath = "/src/Bar.cs",
                    Line = 42,
                    ContainingMethod = "Execute",
                    SourceLine = "DoWork();"
                }
            ]
        };
    }
}
