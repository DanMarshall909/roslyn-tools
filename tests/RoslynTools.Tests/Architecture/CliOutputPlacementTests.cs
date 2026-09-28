using RoslynTools.Core;
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

    [Fact]
    public void ambient_output_context_belongs_to_shared_host_composition()
    {
        typeof(RtContext).Assembly.GetName().Name.ShouldBe("RoslynTools.Hosting");
    }

    [Fact]
    public void ambient_console_and_logging_types_belong_to_shared_host_composition()
    {
        var hosting = typeof(RtContext).Assembly;
        typeof(RtConsole).Assembly.ShouldBe(hosting);
        typeof(IRtConsole).Assembly.ShouldBe(hosting);
        typeof(RtLog).Assembly.ShouldBe(hosting);
        typeof(IRtLogger).Assembly.ShouldBe(hosting);
        typeof(RtLogLevel).Assembly.ShouldBe(hosting);
        typeof(RtOutputContext).Assembly.ShouldBe(hosting);
    }
}
