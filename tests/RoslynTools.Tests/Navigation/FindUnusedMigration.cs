using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Commands.Results;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Tests.Navigation;

public class FindUnusedMigration : IDisposable
{
    public FindUnusedMigration()
    {
        RtContext.Format = OutputFormat.Text;
    }

    public void Dispose()
    {
        RtContext.Format = OutputFormat.Text;
        GC.SuppressFinalize(this);
    }

    private static FindUnusedResult BuildSampleResult()
    {
        var symbols = new List<UnusedSymbol>
        {
            new("UnusedMethod", "method", "/path/to/Services.cs", 42),
            new("OrphanClass", "type", "/path/to/Models.cs", 7)
        };
        return FindUnusedResult.FromSymbols(symbols);
    }

    // ── AC-3: RenderText reproduces prior output byte-for-byte ───────────────

    [Fact]
    public void renders_text_matching_legacy_format_exactly()
    {
        var symbol = new UnusedSymbol("UnusedMethod", "method", "/path/to/Services.cs", 42);
        var result = FindUnusedResult.FromSymbols([symbol]);

        var sw = new StringWriter();
        result.RenderText(sw);

        // Legacy format: "{r.Kind,-10} {r.Name,-40} {r.FilePath}:{r.Line}"
        var expected = $"{"method",-10} {"UnusedMethod",-40} /path/to/Services.cs:42";
        Assert.Equal(expected, sw.ToString().TrimEnd('\r', '\n'));
    }

    [Fact]
    public void renders_text_with_each_symbol_on_own_line()
    {
        var result = BuildSampleResult();
        var sw = new StringWriter();
        result.RenderText(sw);
        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("UnusedMethod", lines[0]);
        Assert.Contains("OrphanClass", lines[1]);
    }

    [Fact]
    public void renders_text_empty_result_as_no_output()
    {
        var result = FindUnusedResult.FromSymbols([]);
        var sw = new StringWriter();
        result.RenderText(sw);
        Assert.Equal(string.Empty, sw.ToString());
    }

    // ── AC-4: --json produces well-formed JSON ────────────────────────────────

    [Fact]
    public void emits_json_output_is_well_formed()
    {
        RtContext.Format = OutputFormat.Json;
        var result = BuildSampleResult();
        var sw = new StringWriter();
        RtSerializer.Write(result, sw);
        var json = sw.ToString();
        Assert.False(string.IsNullOrWhiteSpace(json));
        using var doc = JsonDocument.Parse(json); // throws if malformed
    }

    [Fact]
    public void emits_json_output_contains_symbols_array()
    {
        RtContext.Format = OutputFormat.Json;
        var result = BuildSampleResult();
        var sw = new StringWriter();
        RtSerializer.Write(result, sw);
        var json = sw.ToString();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("symbols", out var symbols),
            "Expected 'symbols' property in JSON output");
    }

    // ── AC-4: --markdown produces well-formed markdown ───────────────────────

    [Fact]
    public void emits_markdown_output_contains_table_header()
    {
        RtContext.Format = OutputFormat.Markdown;
        var result = BuildSampleResult();
        var sw = new StringWriter();
        RtSerializer.Write(result, sw);
        var markdown = sw.ToString();
        Assert.Contains("|", markdown);
    }

    // ── AC-4: --llm produces key:value pairs ─────────────────────────────────

    [Fact]
    public void emits_llm_output_contains_symbol_names()
    {
        RtContext.Format = OutputFormat.Llm;
        var result = BuildSampleResult();
        var sw = new StringWriter();
        RtSerializer.Write(result, sw);
        var text = sw.ToString();
        Assert.Contains("UnusedMethod", text);
        Assert.Contains("OrphanClass", text);
    }

    // ── AC-5: no flag produces unchanged legacy text ─────────────────────────

    [Fact]
    public void handles_default_format_produces_legacy_text()
    {
        // RtContext.Format defaults to Text (set in ctor)
        var result = BuildSampleResult();
        var sw = new StringWriter();
        RtSerializer.Write(result, sw);
        var text = sw.ToString();

        // Should be plain columnar text, not JSON
        Assert.DoesNotContain("{", text);
        Assert.DoesNotContain("#", text);
        Assert.Contains("UnusedMethod", text);
    }

    // ── AC-6: SupportedFormats ────────────────────────────────────────────────

    [Fact]
    public void finds_unused_command_supported_formats_contains_all_four()
    {
        var formats = FindUnusedCommand.Info.SupportedFormats;
        Assert.NotNull(formats);
        Assert.Contains(OutputFormat.Text, formats);
        Assert.Contains(OutputFormat.Json, formats);
        Assert.Contains(OutputFormat.Markdown, formats);
        Assert.Contains(OutputFormat.Llm, formats);
    }

    // ── AC-1: IRtOutput contract ──────────────────────────────────────────────

    [Fact]
    public void finds_unused_result_implements_irt_output()
    {
        var result = BuildSampleResult();
        Assert.IsAssignableFrom<IRtOutput>(result);
    }

    [Fact]
    public void unused_symbol_result_implements_irt_output()
    {
        var symbolResult = new UnusedSymbolResult
        {
            Kind = "method",
            Name = "Foo",
            FilePath = "/a/b.cs",
            Line = 1
        };
        Assert.IsAssignableFrom<IRtOutput>(symbolResult);
    }
}
