using RoslynTools.Services;
using RoslynTools.Services.Suggest;
using Shouldly;

namespace RoslynTools.Tests.Suggest;

public sealed class SuggestionHelperTests
{
    [Fact]
    public void relationship_follow_up_uses_mode_and_symbol_arguments()
    {
        var description = new CompactDescription("Example.Widget", "Class", []);

        var suggestion = SuggestionHelper.ForDescribe(description, "Example.slnx")
            .Single(item => item.ToolName == "relationships");

        suggestion.Arguments["symbol"].ShouldBe("Example.Widget");
        suggestion.Arguments["mode"].ShouldBe("References");
        suggestion.Arguments["solution"].ShouldBe("Example.slnx");
        suggestion.Cli.ShouldBe(
            "rt inspect relationships references \"Example.Widget\" --solution Example.slnx");
    }
}
