using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Commands.Results;
using RoslynTools.Core.Output;
using RoslynTools.Services;
using Shouldly;

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

    // ── AC-3: RenderText preserves the legacy column layout ──────────────────
    // The original migration asserted byte-for-byte equality with the pre-renderer
    // output. Two later changes deliberately extended the line: the suggested-fix
    // catalog appends "  → [tool] ..." continuation lines, and US-138 appends the
    // finding's confidence. What still has to hold is that the legacy prefix — the
    // aligned "{Kind,-10} {Name,-40} {FilePath}:{Line}" columns — is intact, so
    // existing readers of the leading columns keep working.

    [Fact]
    public void renders_text_preserving_the_legacy_column_prefix()
    {
        var symbol = new UnusedSymbol("UnusedMethod", "method", "/path/to/Services.cs", 42);
        var result = FindUnusedResult.FromSymbols([symbol]);

        var sw = new StringWriter();
        result.RenderText(sw);

        var legacyPrefix = $"{"method",-10} {"UnusedMethod",-40} /path/to/Services.cs:42";
        var firstLine = sw.ToString().Split('\n')[0];
        firstLine.ShouldStartWith(legacyPrefix, Case.Sensitive,
            "the aligned legacy columns lead the line; confidence and fixes are additive");
    }

    [Fact]
    public void renders_text_reporting_the_confidence_of_each_finding()
    {
        var symbol = new UnusedSymbol("UnusedMethod", "method", "/path/to/Services.cs", 42);
        var result = FindUnusedResult.FromSymbols([symbol]);

        var sw = new StringWriter();
        result.RenderText(sw);

        sw.ToString().Split('\n')[0].ShouldContain(UnusedConfidence.High, Case.Sensitive,
            "the caller decides what to do with a finding, so text output must state the confidence too");
    }

    [Fact]
    public void renders_text_with_each_symbol_heading_its_own_line()
    {
        var result = BuildSampleResult();
        var sw = new StringWriter();
        result.RenderText(sw);

        // Continuation lines for suggested fixes are indented with "  →"; a finding heads a line.
        var findingLines = sw.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.TrimStart().StartsWith('→'))
            .ToList();

        findingLines.Count.ShouldBe(2, "two symbols means two findings, however many fixes each carries");
        findingLines[0].ShouldContain("UnusedMethod", Case.Sensitive);
        findingLines[1].ShouldContain("OrphanClass", Case.Sensitive);
    }

    [Fact]
    public void renders_text_empty_result_as_no_output()
    {
        var result = FindUnusedResult.FromSymbols([]);
        var sw = new StringWriter();
        result.RenderText(sw);
        sw.ToString().ShouldBe(string.Empty);
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
        string.IsNullOrWhiteSpace(json).ShouldBeFalse();
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

    [Fact]
    public void ordinary_json_output_omits_profile_when_profiling_was_not_requested()
    {
        RtContext.Format = OutputFormat.Json;
        var writer = new StringWriter();
        RtSerializer.Write(BuildSampleResult(), writer);

        using var document = JsonDocument.Parse(writer.ToString());
        document.RootElement.TryGetProperty("profile", out _).ShouldBeFalse();
    }

    [Fact]
    public void requested_profile_json_contains_reference_search_count()
    {
        RtContext.Format = OutputFormat.Json;
        var result = BuildSampleResult() with
        {
            Profile = new FindUnusedProfile(17, 2, 3, 4, 0, 1, 1, 2, 5, 0, 12)
        };
        var writer = new StringWriter();
        RtSerializer.Write(result, writer);

        using var document = JsonDocument.Parse(writer.ToString());
        document.RootElement.GetProperty("profile").GetProperty("referenceQueries")
            .GetInt32().ShouldBe(5);
    }

    [Theory]
    [InlineData("Markdown")]
    [InlineData("Llm")]
    public void requested_profile_is_visible_in_supported_readable_formats(string format)
    {
        RtContext.Format = Enum.Parse<OutputFormat>(format);
        var result = BuildSampleResult() with
        {
            Profile = new FindUnusedProfile(17, 2, 3, 4, 0, 1, 1, 2, 5, 0, 12)
        };
        var writer = new StringWriter();

        RtSerializer.Write(result, writer);

        writer.ToString().ShouldContain("profile", Case.Insensitive);
        writer.ToString().ShouldContain("5 reference searches", Case.Sensitive);
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
        markdown.ShouldContain("|", Case.Sensitive);
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
        text.ShouldContain("UnusedMethod", Case.Sensitive);
        text.ShouldContain("OrphanClass", Case.Sensitive);
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
        text.ShouldNotContain("{", Case.Sensitive);
        text.ShouldNotContain("#", Case.Sensitive);
        text.ShouldContain("UnusedMethod", Case.Sensitive);
    }

    // ── AC-6: SupportedFormats ────────────────────────────────────────────────

    [Fact]
    public void finds_unused_command_supported_formats_contains_all_four()
    {
        var formats = FindUnusedCommand.Info.SupportedFormats;
        formats.ShouldNotBeNull();
        formats.ShouldContain(OutputFormat.Text);
        formats.ShouldContain(OutputFormat.Json);
        formats.ShouldContain(OutputFormat.Markdown);
        formats.ShouldContain(OutputFormat.Llm);
    }

    // ── AC-1: IRtOutput contract ──────────────────────────────────────────────

    [Fact]
    public void finds_unused_result_implements_irt_output()
    {
        var result = BuildSampleResult();
        result.ShouldBeAssignableTo<IRtOutput>();
    }

    [Fact]
    public void unused_symbol_result_implements_irt_output()
    {
        var symbolResult = new UnusedSymbolResult
        {
            Kind = "method",
            Name = "Foo",
            FilePath = "/a/b.cs",
            Line = 1,
            Confidence = UnusedConfidence.High,
            Reason = UnusedReason.NoSourceReferences
        };
        symbolResult.ShouldBeAssignableTo<IRtOutput>();
    }
}
