using RoslynTools.Services;
using RoslynTools.Services.Suggest;
using Shouldly;

namespace RoslynTools.Tests.Suggest;

public sealed class SuggestionHelperTests
{
    [Fact]
    public void analyze_interface_follow_up_uses_the_required_interface_argument()
    {
        var description = new CompactDescription("Example.Widget", "Class", []);

        var suggestion = SuggestionHelper.ForDescribe(description, "Example.slnx")
            .Single(item => item.ToolName == "analyze_interface");

        suggestion.Arguments.ShouldContainKey("interface");
        suggestion.Arguments["interface"].ShouldBe("Example.Widget");
        suggestion.Arguments.ShouldNotContainKey("symbol");
        suggestion.Arguments["solution"].ShouldBe("Example.slnx");
    }
}
