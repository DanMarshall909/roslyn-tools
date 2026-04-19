using System.Text.Json;
using RoslynTools.Commands.Results;
using RoslynTools.Core.Output;

namespace RoslynTools.Tests.Navigation;

public sealed class FindRefsMigrationTests : IDisposable
{
    public FindRefsMigrationTests()
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
    public void RenderText_reproduces_legacy_format_for_each_reference()
    {
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        result.RenderText(sw);

        var output = sw.ToString();
        Assert.Contains("/src/Service.cs(15): var svc = new MyService();", output);
        Assert.Contains("/src/Controller.cs(88): _svc.Process();", output);
    }

    [Fact]
    public void RenderText_uses_parentheses_not_colon_for_line_number()
    {
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        result.RenderText(sw);

        var output = sw.ToString();
        // find-refs format is path(line): text — NOT path:line: text
        Assert.Contains("(15):", output);
        Assert.DoesNotContain(":15:", output);
    }

    [Fact]
    public void RenderText_appends_summary_line_with_count_and_symbol_name()
    {
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        result.RenderText(sw);

        var output = sw.ToString();
        Assert.Contains("Found 2 reference(s) to 'MyService'.", output);
    }

    [Fact]
    public void RenderText_summary_is_preceded_by_blank_line()
    {
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        result.RenderText(sw);

        var output = sw.ToString();
        // The original code wrote $"\nFound..." which gives a blank line before the summary
        Assert.Contains("\nFound", output);
    }

    // ── ReferenceResult.RenderText byte-for-byte ──────────────────────────────────

    [Fact]
    public void ReferenceResult_RenderText_produces_exact_format()
    {
        var reference = new ReferenceResult
        {
            FilePath = "C:/code/Foo.cs",
            Line = 7,
            LineText = "var x = new Foo();",
        };
        using var sw = new StringWriter();

        reference.RenderText(sw);

        Assert.Equal("C:/code/Foo.cs(7): var x = new Foo();", sw.ToString());
    }

    // ── JSON format ───────────────────────────────────────────────────────────────

    [Fact]
    public void Json_output_contains_symbolName_and_references_array()
    {
        RtContext.Format = OutputFormat.Json;
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var doc = JsonDocument.Parse(sw.ToString());
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("SymbolName", out var sym));
        Assert.Equal("MyService", sym.GetString());

        Assert.True(root.TryGetProperty("References", out var refs));
        Assert.Equal(JsonValueKind.Array, refs.ValueKind);
        Assert.Equal(2, refs.GetArrayLength());
    }

    [Fact]
    public void Json_reference_entries_contain_expected_fields()
    {
        RtContext.Format = OutputFormat.Json;
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var doc = JsonDocument.Parse(sw.ToString());
        var first = doc.RootElement.GetProperty("References")[0];

        Assert.True(first.TryGetProperty("FilePath", out _));
        Assert.True(first.TryGetProperty("Line", out _));
        Assert.True(first.TryGetProperty("LineText", out _));
    }

    // ── Markdown format ───────────────────────────────────────────────────────────

    [Fact]
    public void Markdown_output_contains_reference_data()
    {
        RtContext.Format = OutputFormat.Markdown;
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var md = sw.ToString();
        Assert.Contains("MyService", md);
        Assert.Contains("/src/Service.cs", md);
    }

    // ── LLM format ───────────────────────────────────────────────────────────────

    [Fact]
    public void Llm_output_contains_symbolName()
    {
        RtContext.Format = OutputFormat.Llm;
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var llm = sw.ToString();
        Assert.Contains("MyService", llm);
    }

    [Fact]
    public void Llm_output_contains_reference_filePaths()
    {
        RtContext.Format = OutputFormat.Llm;
        var result = MakeFindRefsResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var llm = sw.ToString();
        Assert.Contains("/src/Service.cs", llm);
        Assert.Contains("/src/Controller.cs", llm);
    }

    // ── SupportedFormats ─────────────────────────────────────────────────────────

    [Fact]
    public void FindRefsCommand_Info_SupportedFormats_contains_all_four_formats()
    {
        var formats = RoslynTools.Commands.FindRefsCommand.Info.SupportedFormats;
        Assert.NotNull(formats);
        Assert.Contains(OutputFormat.Text, formats);
        Assert.Contains(OutputFormat.Json, formats);
        Assert.Contains(OutputFormat.Markdown, formats);
        Assert.Contains(OutputFormat.Llm, formats);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static FindRefsResult MakeFindRefsResult() =>
        new()
        {
            SymbolName = "MyService",
            References =
            [
                new ReferenceResult
                {
                    FilePath = "/src/Service.cs",
                    Line = 15,
                    LineText = "var svc = new MyService();",
                },
                new ReferenceResult
                {
                    FilePath = "/src/Controller.cs",
                    Line = 88,
                    LineText = "_svc.Process();",
                },
            ],
        };
}
