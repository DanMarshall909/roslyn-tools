using System.Reflection;
using RoslynTools.Core.Output;

namespace RoslynTools.Tests;

public sealed class OutputLayer : IDisposable
{
    public OutputLayer()
    {
        RtContext.Format = OutputFormat.Text;
        RtContext.Level = OutputLevel.Default;
    }

    public void Dispose()
    {
        RtContext.Format = OutputFormat.Text;
        RtContext.Level = OutputLevel.Default;
    }

    // ── AC-8: RtContext defaults ───────────────────────────────────────────────────

    [Fact]
    public void handles_rt_context_defaults_to_text_format_and_default_level()
    {
        Assert.Equal(OutputFormat.Text, RtContext.Format);
        Assert.Equal(OutputLevel.Default, RtContext.Level);
    }

    // ── AC-9: RtContext properties are settable ────────────────────────────────────

    [Fact]
    public void handles_rt_context_format_and_level_are_settable()
    {
        RtContext.Format = OutputFormat.Json;
        RtContext.Level = OutputLevel.Detailed;

        Assert.Equal(OutputFormat.Json, RtContext.Format);
        Assert.Equal(OutputLevel.Detailed, RtContext.Level);
    }

    // ── AC-10: RtSerializer dispatches Text to IRtOutput.RenderText ───────────────

    [Fact]
    public void handles_rt_serializer_write_with_text_format_calls_render_text()
    {
        RtContext.Format = OutputFormat.Text;
        var output = new HelloOutput();
        using var sw = new StringWriter();

        RtSerializer.Write(output, sw);

        Assert.Equal("HELLO", sw.ToString());
    }

    // ── AC-11: OutputFieldAttribute is readable via reflection ────────────────────

    [Fact]
    public void preserves_output_field_level_and_description_via_reflection()
    {
        var prop = typeof(AnnotatedModel).GetProperty(nameof(AnnotatedModel.Name))!;
        var attr = prop.GetCustomAttribute<OutputFieldAttribute>();

        Assert.NotNull(attr);
        Assert.Equal(OutputLevel.Detailed, attr!.Level);
        Assert.Equal("The name field", attr.Description);
    }

    // ── AC-2: OutputLevel has explicit integer values ──────────────────────────────

    [Fact]
    public void has_expected_integer_values_for_output_level()
    {
        Assert.Equal(0, (int)OutputLevel.Llm);
        Assert.Equal(1, (int)OutputLevel.Default);
        Assert.Equal(2, (int)OutputLevel.Detailed);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private sealed class HelloOutput : IRtOutput
    {
        public void RenderText(TextWriter writer)
        {
            writer.Write("HELLO");
        }
    }

    private sealed class AnnotatedModel
    {
        [OutputField(OutputLevel.Detailed, "The name field")]
        public string Name { get; set; } = string.Empty;
    }
}
