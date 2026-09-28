using RoslynTools.Core.Output;
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
}
