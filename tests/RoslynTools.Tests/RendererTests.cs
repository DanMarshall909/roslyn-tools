using System.Text.Json;
using RoslynTools.Core.Output;

namespace RoslynTools.Tests;

public sealed class RendererTests : IDisposable
{
    public RendererTests()
    {
        RtContext.Format = OutputFormat.Text;
        RtContext.Level = OutputLevel.Default;
    }

    public void Dispose()
    {
        RtContext.Format = OutputFormat.Text;
        RtContext.Level = OutputLevel.Default;
    }

    // ── AC-5: JSON includes Llm + Default + Detailed fields ───────────────────────

    [Fact]
    public void JsonRenderer_includes_Llm_Default_and_Detailed_fields()
    {
        RtContext.Format = OutputFormat.Json;
        var result = new FakeResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var json = sw.ToString();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("llmField", out _), "Expected llmField in JSON");
        Assert.True(root.TryGetProperty("defaultField", out _), "Expected defaultField in JSON");
        Assert.True(root.TryGetProperty("detailedField", out _), "Expected detailedField in JSON");
    }

    // ── AC-6: Markdown includes Llm + Default fields, omits Detailed ──────────────

    [Fact]
    public void MarkdownRenderer_includes_Llm_and_Default_fields_omits_Detailed()
    {
        RtContext.Format = OutputFormat.Markdown;
        var result = new FakeResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var md = sw.ToString();

        Assert.Contains("llm-value", md);
        Assert.Contains("default-value", md);
        Assert.DoesNotContain("detailed-value", md);
    }

    // ── AC-7: LLM output contains only Llm-level fields ──────────────────────────

    [Fact]
    public void LlmRenderer_emits_only_Llm_level_fields()
    {
        RtContext.Format = OutputFormat.Llm;
        var result = new FakeResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var output = sw.ToString();

        Assert.Contains("llm-value", output);
        Assert.DoesNotContain("default-value", output);
        Assert.DoesNotContain("detailed-value", output);
    }

    // ── AC-8: IReadOnlyList<FakeChild> renders as JSON array and Markdown table ───

    [Fact]
    public void JsonRenderer_renders_child_collection_as_array()
    {
        RtContext.Format = OutputFormat.Json;
        var result = new FakeParent();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var json = sw.ToString();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("children", out var children));
        Assert.Equal(JsonValueKind.Array, children.ValueKind);
        Assert.Equal(2, children.GetArrayLength());
    }

    [Fact]
    public void MarkdownRenderer_renders_child_collection_as_table()
    {
        RtContext.Format = OutputFormat.Markdown;
        var result = new FakeParent();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var md = sw.ToString();

        Assert.Contains("| name |", md);
        Assert.Contains("Alice", md);
        Assert.Contains("Bob", md);
    }

    // ── YAML: includes Llm + Default + Detailed fields ────────────────────────────

    [Fact]
    public void YamlRenderer_includes_Llm_Default_and_Detailed_fields()
    {
        RtContext.Format = OutputFormat.Yaml;
        var result = new FakeResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var yaml = sw.ToString();
        Assert.Contains("llmField:", yaml);
        Assert.Contains("defaultField:", yaml);
        Assert.Contains("detailedField:", yaml);
        Assert.Contains("llm-value", yaml);
        Assert.Contains("default-value", yaml);
        Assert.Contains("detailed-value", yaml);
    }

    [Fact]
    public void YamlRenderer_renders_child_collection_as_list()
    {
        RtContext.Format = OutputFormat.Yaml;
        var result = new FakeParent();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var yaml = sw.ToString();
        Assert.Contains("children:", yaml);
        Assert.Contains("Alice", yaml);
        Assert.Contains("Bob", yaml);
    }

    [Fact]
    public void YamlRenderer_omits_property_without_OutputField()
    {
        RtContext.Format = OutputFormat.Yaml;
        var result = new FakeResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        Assert.DoesNotContain("untagged-value", sw.ToString());
    }

    [Fact]
    public void YamlRenderer_renders_null_nested_IRtOutput_as_yaml_null()
    {
        RtContext.Format = OutputFormat.Yaml;
        var result = new FakeWithNullChild();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var yaml = sw.ToString();
        Assert.Contains("child:", yaml);
    }

    // ── AC-9: Text rendering delegates to RenderText exactly once ─────────────────

    [Fact]
    public void TextRenderer_delegates_to_RenderText_and_writes_nothing_else()
    {
        RtContext.Format = OutputFormat.Text;
        var result = new TrackingOutput();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        Assert.Equal(1, result.RenderTextCallCount);
        Assert.Equal("TRACKED", sw.ToString());
    }

    // ── AC-10: Property without [OutputField] is omitted from all renderers ───────

    [Fact]
    public void JsonRenderer_omits_property_without_OutputField()
    {
        RtContext.Format = OutputFormat.Json;
        var result = new FakeResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var json = sw.ToString();
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("UntaggedField", out _));
    }

    [Fact]
    public void MarkdownRenderer_omits_property_without_OutputField()
    {
        RtContext.Format = OutputFormat.Markdown;
        var result = new FakeResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        Assert.DoesNotContain("untagged-value", sw.ToString());
    }

    [Fact]
    public void LlmRenderer_omits_property_without_OutputField()
    {
        RtContext.Format = OutputFormat.Llm;
        var result = new FakeResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        Assert.DoesNotContain("untagged-value", sw.ToString());
    }

    // ── AC-11: Null IRtOutput renders as JSON null and omitted Llm line ───────────

    [Fact]
    public void JsonRenderer_renders_null_nested_IRtOutput_as_json_null()
    {
        RtContext.Format = OutputFormat.Json;
        var result = new FakeWithNullChild();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var doc = JsonDocument.Parse(sw.ToString());
        Assert.True(doc.RootElement.TryGetProperty("child", out var child));
        Assert.Equal(JsonValueKind.Null, child.ValueKind);
    }

    [Fact]
    public void LlmRenderer_omits_null_IRtOutput_field()
    {
        RtContext.Format = OutputFormat.Llm;
        var result = new FakeWithNullChild();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        Assert.DoesNotContain("Child", sw.ToString());
    }

    // ── Fixture types ─────────────────────────────────────────────────────────────

    private sealed class FakeResult : IRtOutput
    {
        [OutputField(OutputLevel.Llm, "llmField")]
        public string LlmField { get; } = "llm-value";

        [OutputField(OutputLevel.Default, "defaultField")]
        public string DefaultField { get; } = "default-value";

        [OutputField(OutputLevel.Detailed, "detailedField")]
        public string DetailedField { get; } = "detailed-value";

        public string UntaggedField { get; } = "untagged-value";

        public void RenderText(TextWriter writer)
        {
            writer.Write("fake");
        }
    }

    private sealed class FakeChild : IRtOutput
    {
        public FakeChild(string name)
        {
            Name = name;
        }

        [OutputField(OutputLevel.Default, "name")]
        public string Name { get; }

        public void RenderText(TextWriter writer)
        {
            writer.Write(Name);
        }
    }

    private sealed class FakeParent : IRtOutput
    {
        [OutputField(OutputLevel.Default, "children")]
        public IReadOnlyList<FakeChild> Children { get; } =
            [new("Alice"), new("Bob")];

        public void RenderText(TextWriter writer)
        {
            writer.Write("parent");
        }
    }

    private sealed class FakeWithNullChild : IRtOutput
    {
        [OutputField(OutputLevel.Llm, "child")]
        public FakeChild? Child { get; } = null;

        public void RenderText(TextWriter writer)
        {
            writer.Write("null-child");
        }
    }

    private sealed class TrackingOutput : IRtOutput
    {
        public int RenderTextCallCount { get; private set; }

        public void RenderText(TextWriter writer)
        {
            RenderTextCallCount++;
            writer.Write("TRACKED");
        }
    }
}
