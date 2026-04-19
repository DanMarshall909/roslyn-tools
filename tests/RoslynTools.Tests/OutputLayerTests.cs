using System.Reflection;
using RoslynTools.Core.Output;

namespace RoslynTools.Tests;

public sealed class OutputLayerTests : IDisposable
{
    public OutputLayerTests()
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
    public void RtContext_defaults_to_Text_format_and_Default_level()
    {
        Assert.Equal(OutputFormat.Text, RtContext.Format);
        Assert.Equal(OutputLevel.Default, RtContext.Level);
    }

    // ── AC-9: RtContext properties are settable ────────────────────────────────────

    [Fact]
    public void RtContext_Format_and_Level_are_settable()
    {
        RtContext.Format = OutputFormat.Json;
        RtContext.Level = OutputLevel.Detailed;

        Assert.Equal(OutputFormat.Json, RtContext.Format);
        Assert.Equal(OutputLevel.Detailed, RtContext.Level);
    }

    // ── AC-10: RtSerializer dispatches Text to IRtOutput.RenderText ───────────────

    [Fact]
    public void RtSerializer_Write_with_Text_format_calls_RenderText()
    {
        RtContext.Format = OutputFormat.Text;
        var output = new HelloOutput();
        using var sw = new StringWriter();

        RtSerializer.Write(output, sw);

        Assert.Equal("HELLO", sw.ToString());
    }

    // ── AC-11: OutputFieldAttribute is readable via reflection ────────────────────

    [Fact]
    public void OutputFieldAttribute_preserves_level_and_description_via_reflection()
    {
        var prop = typeof(AnnotatedModel).GetProperty(nameof(AnnotatedModel.Name))!;
        var attr = prop.GetCustomAttribute<OutputFieldAttribute>();

        Assert.NotNull(attr);
        Assert.Equal(OutputLevel.Detailed, attr!.Level);
        Assert.Equal("The name field", attr.Description);
    }

    // ── AC-2: OutputLevel has explicit integer values ──────────────────────────────

    [Fact]
    public void OutputLevel_has_expected_integer_values()
    {
        Assert.Equal(0, (int)OutputLevel.Llm);
        Assert.Equal(1, (int)OutputLevel.Default);
        Assert.Equal(2, (int)OutputLevel.Detailed);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private sealed class HelloOutput : IRtOutput
    {
        public void RenderText(TextWriter writer) => writer.Write("HELLO");
    }

    private sealed class AnnotatedModel
    {
        [OutputField(OutputLevel.Detailed, "The name field")]
        public string Name { get; set; } = string.Empty;
    }
}
