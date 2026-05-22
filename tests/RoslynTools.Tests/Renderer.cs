using System.Text.Json;
using RoslynTools.Core.Output;
using Shouldly;

namespace RoslynTools.Tests;

public sealed class Renderer : IDisposable
{
    public Renderer()
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
    public void emits_json_renderer_includes_llm_default_and_detailed_fields()
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
    public void emits_markdown_renderer_includes_llm_and_default_fields_omits_detailed()
    {
        RtContext.Format = OutputFormat.Markdown;
        var result = new FakeResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var md = sw.ToString();

        md.ShouldContain("llm-value", Case.Sensitive);
        md.ShouldContain("default-value", Case.Sensitive);
        md.ShouldNotContain("detailed-value", Case.Sensitive);
    }

    // ── AC-7: LLM output contains only Llm-level fields ──────────────────────────

    [Fact]
    public void emits_llm_renderer_emits_only_llm_level_fields()
    {
        RtContext.Format = OutputFormat.Llm;
        var result = new FakeResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var output = sw.ToString();

        output.ShouldContain("llm-value", Case.Sensitive);
        output.ShouldNotContain("default-value", Case.Sensitive);
        output.ShouldNotContain("detailed-value", Case.Sensitive);
    }

    // ── AC-8: IReadOnlyList<FakeChild> renders as JSON array and Markdown table ───

    [Fact]
    public void emits_json_renderer_renders_child_collection_as_array()
    {
        RtContext.Format = OutputFormat.Json;
        var result = new FakeParent();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var json = sw.ToString();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("children", out var children).ShouldBeTrue();
        children.ValueKind.ShouldBe(JsonValueKind.Array);
        children.GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public void emits_markdown_renderer_renders_child_collection_as_table()
    {
        RtContext.Format = OutputFormat.Markdown;
        var result = new FakeParent();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var md = sw.ToString();

        md.ShouldContain("| name |", Case.Sensitive);
        md.ShouldContain("Alice", Case.Sensitive);
        md.ShouldContain("Bob", Case.Sensitive);
    }

    // ── YAML: includes Llm + Default + Detailed fields ────────────────────────────

    [Fact]
    public void emits_yaml_renderer_includes_llm_default_and_detailed_fields()
    {
        RtContext.Format = OutputFormat.Yaml;
        var result = new FakeResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var yaml = sw.ToString();
        yaml.ShouldContain("llmField:", Case.Sensitive);
        yaml.ShouldContain("defaultField:", Case.Sensitive);
        yaml.ShouldContain("detailedField:", Case.Sensitive);
        yaml.ShouldContain("llm-value", Case.Sensitive);
        yaml.ShouldContain("default-value", Case.Sensitive);
        yaml.ShouldContain("detailed-value", Case.Sensitive);
    }

    [Fact]
    public void emits_yaml_renderer_renders_child_collection_as_list()
    {
        RtContext.Format = OutputFormat.Yaml;
        var result = new FakeParent();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var yaml = sw.ToString();
        yaml.ShouldContain("children:", Case.Sensitive);
        yaml.ShouldContain("Alice", Case.Sensitive);
        yaml.ShouldContain("Bob", Case.Sensitive);
    }

    [Fact]
    public void emits_yaml_renderer_omits_property_without_output_field()
    {
        RtContext.Format = OutputFormat.Yaml;
        var result = new FakeResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        sw.ToString().ShouldNotContain("untagged-value", Case.Sensitive);
    }

    [Fact]
    public void emits_yaml_renderer_renders_null_nested_irt_output_as_yaml_null()
    {
        RtContext.Format = OutputFormat.Yaml;
        var result = new FakeWithNullChild();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var yaml = sw.ToString();
        yaml.ShouldContain("child:", Case.Sensitive);
    }

    // ── AC-9: Text rendering delegates to RenderText exactly once ─────────────────

    [Fact]
    public void delegates_text_renderer_to_render_text_without_extra_output()
    {
        RtContext.Format = OutputFormat.Text;
        var result = new TrackingOutput();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        result.RenderTextCallCount.ShouldBe(1);
        sw.ToString().ShouldBe("TRACKED");
    }

    // ── AC-10: Property without [OutputField] is omitted from all renderers ───────

    [Fact]
    public void emits_json_renderer_omits_property_without_output_field()
    {
        RtContext.Format = OutputFormat.Json;
        var result = new FakeResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var json = sw.ToString();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("UntaggedField", out _).ShouldBeFalse();
    }

    [Fact]
    public void emits_markdown_renderer_omits_property_without_output_field()
    {
        RtContext.Format = OutputFormat.Markdown;
        var result = new FakeResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        sw.ToString().ShouldNotContain("untagged-value", Case.Sensitive);
    }

    [Fact]
    public void emits_llm_renderer_omits_property_without_output_field()
    {
        RtContext.Format = OutputFormat.Llm;
        var result = new FakeResult();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        sw.ToString().ShouldNotContain("untagged-value", Case.Sensitive);
    }

    // ── AC-11: Null IRtOutput renders as JSON null and omitted Llm line ───────────

    [Fact]
    public void emits_json_renderer_renders_null_nested_irt_output_as_json_null()
    {
        RtContext.Format = OutputFormat.Json;
        var result = new FakeWithNullChild();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        var doc = JsonDocument.Parse(sw.ToString());
        doc.RootElement.TryGetProperty("child", out var child).ShouldBeTrue();
        child.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void emits_llm_renderer_omits_null_irt_output_field()
    {
        RtContext.Format = OutputFormat.Llm;
        var result = new FakeWithNullChild();
        using var sw = new StringWriter();

        RtSerializer.Write(result, sw);

        sw.ToString().ShouldNotContain("Child", Case.Sensitive);
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
