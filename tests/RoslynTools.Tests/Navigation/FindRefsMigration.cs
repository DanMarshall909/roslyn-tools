using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Core.Output;
using RoslynTools.Services.Results;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class FindRefsMigration : IDisposable
{
    public FindRefsMigration()
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
    public void renders_text_matching_legacy_format_for_each_reference()
    {
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        result.RenderText(sw);

        var output = sw.ToString();
        output.ShouldContain("/src/Service.cs(15): var svc = new MyService();", Case.Sensitive);
        output.ShouldContain("/src/Controller.cs(88): _svc.Process();", Case.Sensitive);
    }

    [Fact]
    public void renders_text_line_number_in_parentheses()
    {
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        result.RenderText(sw);

        var output = sw.ToString();
        // find-refs format is path(line): text — NOT path:line: text
        output.ShouldContain("(15):", Case.Sensitive);
        output.ShouldNotContain(":15:", Case.Sensitive);
    }

    [Fact]
    public void renders_text_summary_line_with_count_and_symbol_name()
    {
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        result.RenderText(sw);

        var output = sw.ToString();
        output.ShouldContain("Found 2 reference(s) to 'MyService'.", Case.Sensitive);
    }

    [Fact]
    public void renders_text_summary_after_blank_line()
    {
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        result.RenderText(sw);

        var output = sw.ToString();
        // The original code wrote $"\nFound..." which gives a blank line before the summary
        output.ShouldContain("\nFound", Case.Sensitive);
    }

    // ── ReferenceResult.RenderText byte-for-byte ──────────────────────────────────

    [Fact]
    public void reference_result_render_text_produces_exact_format()
    {
        var reference = new ReferenceResult
        {
            FilePath = "C:/code/Foo.cs",
            Line = 7,
            LineText = "var x = new Foo();"
        };
        using var sw = new StringWriter();

        reference.RenderText(sw);

        sw.ToString().ShouldBe("C:/code/Foo.cs(7): var x = new Foo();");
    }

    // ── JSON format ───────────────────────────────────────────────────────────────

    [Fact]
    public void emits_json_output_contains_symbol_name_and_references_array()
    {
        RtContext.Format = OutputFormat.Json;
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var doc = JsonDocument.Parse(sw.ToString());
        var root = doc.RootElement;

        root.TryGetProperty("symbolName", out var sym).ShouldBeTrue();
        sym.GetString().ShouldBe("MyService");

        root.TryGetProperty("references", out var refs).ShouldBeTrue();
        refs.ValueKind.ShouldBe(JsonValueKind.Array);
        refs.GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public void emits_json_output_contains_fully_qualified_name()
    {
        RtContext.Format = OutputFormat.Json;
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var doc = JsonDocument.Parse(sw.ToString());
        doc.RootElement.TryGetProperty("fullyQualifiedName", out var fqn).ShouldBeTrue();
        fqn.GetString().ShouldBe("TestApp.MyService");
    }

    [Fact]
    public void emits_json_reference_entries_contain_expected_fields()
    {
        RtContext.Format = OutputFormat.Json;
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var doc = JsonDocument.Parse(sw.ToString());
        var first = doc.RootElement.GetProperty("references")[0];

        first.TryGetProperty("filePath", out _).ShouldBeTrue();
        first.TryGetProperty("line", out _).ShouldBeTrue();
        first.TryGetProperty("lineText", out _).ShouldBeTrue();
    }

    // ── Markdown format ───────────────────────────────────────────────────────────

    [Fact]
    public void emits_markdown_output_contains_reference_data()
    {
        RtContext.Format = OutputFormat.Markdown;
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var md = sw.ToString();
        md.ShouldContain("MyService", Case.Sensitive);
        md.ShouldContain("/src/Service.cs", Case.Sensitive);
    }

    // ── LLM format ───────────────────────────────────────────────────────────────

    [Fact]
    public void emits_llm_output_contains_symbol_name()
    {
        RtContext.Format = OutputFormat.Llm;
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var llm = sw.ToString();
        llm.ShouldContain("MyService", Case.Sensitive);
    }

    [Fact]
    public void emits_llm_output_contains_reference_file_paths()
    {
        RtContext.Format = OutputFormat.Llm;
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var llm = sw.ToString();
        llm.ShouldContain("/src/Service.cs", Case.Sensitive);
        llm.ShouldContain("/src/Controller.cs", Case.Sensitive);
    }

    // ── SupportedFormats ─────────────────────────────────────────────────────────

    [Fact]
    public void finds_refs_command_info_supported_formats_contains_all_four_formats()
    {
        var formats = FindRefsCommand.Info.SupportedFormats;
        formats.ShouldNotBeNull();
        formats.ShouldContain(OutputFormat.Text);
        formats.ShouldContain(OutputFormat.Json);
        formats.ShouldContain(OutputFormat.Markdown);
        formats.ShouldContain(OutputFormat.Llm);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static FindRefsResult MakeFindRefsResult()
    {
        return new FindRefsResult
        {
            SymbolName = "MyService",
            FullyQualifiedName = "TestApp.MyService",
            References =
            [
                new ReferenceResult
                {
                    FilePath = "/src/Service.cs",
                    Line = 15,
                    LineText = "var svc = new MyService();"
                },
                new ReferenceResult
                {
                    FilePath = "/src/Controller.cs",
                    Line = 88,
                    LineText = "_svc.Process();"
                }
            ]
        };
    }
}
