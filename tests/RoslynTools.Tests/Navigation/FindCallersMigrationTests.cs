using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Core.Output;
using RoslynTools.Services.Results;

namespace RoslynTools.Tests.Navigation;

public sealed class FindCallersMigrationTests : IDisposable
{
    public FindCallersMigrationTests()
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
    public void RenderText_reproduces_legacy_format_for_each_caller()
    {
        var result = MakeFindCallersResult();
        using var sw = new StringWriter();

        result.RenderText(sw);

        var output = sw.ToString();
        Assert.Contains("/src/Foo.cs:10: RunJob: DoWork();", output);
        Assert.Contains("/src/Bar.cs:42: Execute: DoWork();", output);
    }

    [Fact]
    public void RenderText_each_line_ends_with_newline()
    {
        var result = MakeFindCallersResult();
        using var sw = new StringWriter();

        result.RenderText(sw);

        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
    }

    // ── CallerResult.RenderText byte-for-byte ─────────────────────────────────────

    [Fact]
    public void CallerResult_RenderText_produces_exact_format()
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

        Assert.Equal("C:/code/Foo.cs:7: MyMethod: foo.Bar();", sw.ToString());
    }

    // ── JSON format ───────────────────────────────────────────────────────────────

    [Fact]
    public void Json_output_contains_symbolName_and_callers_array()
    {
        RtContext.Format = OutputFormat.Json;
        var result = MakeFindCallersResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var doc = JsonDocument.Parse(sw.ToString());
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("symbolName", out var sym));
        Assert.Equal("DoWork", sym.GetString());

        Assert.True(root.TryGetProperty("callers", out var callers));
        Assert.Equal(JsonValueKind.Array, callers.ValueKind);
        Assert.Equal(2, callers.GetArrayLength());
    }

    [Fact]
    public void Json_caller_entries_contain_expected_fields()
    {
        RtContext.Format = OutputFormat.Json;
        var result = MakeFindCallersResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var doc = JsonDocument.Parse(sw.ToString());
        var first = doc.RootElement.GetProperty("callers")[0];

        Assert.True(first.TryGetProperty("filePath", out _));
        Assert.True(first.TryGetProperty("line", out _));
        Assert.True(first.TryGetProperty("containingMethod", out _));
        Assert.True(first.TryGetProperty("sourceLine", out _));
    }

    // ── Markdown format ───────────────────────────────────────────────────────────

    [Fact]
    public void Markdown_output_contains_caller_data()
    {
        RtContext.Format = OutputFormat.Markdown;
        var result = MakeFindCallersResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var md = sw.ToString();
        Assert.Contains("RunJob", md);
        Assert.Contains("Execute", md);
    }

    // ── LLM format ───────────────────────────────────────────────────────────────

    [Fact]
    public void Llm_output_contains_symbolName()
    {
        RtContext.Format = OutputFormat.Llm;
        var result = MakeFindCallersResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var llm = sw.ToString();
        Assert.Contains("DoWork", llm);
    }

    [Fact]
    public void Llm_output_contains_caller_filePaths()
    {
        RtContext.Format = OutputFormat.Llm;
        var result = MakeFindCallersResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var llm = sw.ToString();
        Assert.Contains("/src/Foo.cs", llm);
        Assert.Contains("/src/Bar.cs", llm);
    }

    // ── SupportedFormats ─────────────────────────────────────────────────────────

    [Fact]
    public void FindCallersCommand_Info_SupportedFormats_contains_all_four_formats()
    {
        var formats = FindCallersCommand.Info.SupportedFormats;
        Assert.NotNull(formats);
        Assert.Contains(OutputFormat.Text, formats);
        Assert.Contains(OutputFormat.Json, formats);
        Assert.Contains(OutputFormat.Markdown, formats);
        Assert.Contains(OutputFormat.Llm, formats);
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
