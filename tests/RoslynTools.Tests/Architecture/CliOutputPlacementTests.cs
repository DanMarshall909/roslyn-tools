using RoslynTools.Core.Output;
using RoslynTools.Core.Output.Renderers;
using Shouldly;

namespace RoslynTools.Tests.Architecture;

public sealed class CliOutputPlacementTests
{
    [Fact]
    public void process_output_implementations_belong_to_the_cli_adapter()
    {
        var cli = typeof(App).Assembly;
        typeof(RtResultRenderer).Assembly.ShouldBe(cli);
        typeof(RtSerializer).Assembly.ShouldBe(cli);
        typeof(StructuredOutput).Assembly.ShouldBe(cli);
    }

    [Fact]
    public void format_renderers_belong_to_the_cli_adapter()
    {
        var cli = typeof(App).Assembly;
        typeof(JsonRenderer).Assembly.ShouldBe(cli);
        typeof(LlmRenderer).Assembly.ShouldBe(cli);
        typeof(MarkdownRenderer).Assembly.ShouldBe(cli);
        typeof(TextRenderer).Assembly.ShouldBe(cli);
        typeof(YamlRenderer).Assembly.ShouldBe(cli);
    }
}
