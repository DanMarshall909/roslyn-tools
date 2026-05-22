using RoslynTools.Commands;
using Shouldly;

namespace RoslynTools.Tests.Refactor;

public class DestaticClass
{
    [Fact]
    [Trait("Concern", "Metadata")]
    public void command_metadata_category_is_refactor_and_mutates()
    {
        var info = DestaticClassCommand.Info;

        info.Name.ShouldBe("convert-static-class");
        info.Category.ShouldBe("refactor");
        info.Mutates.ShouldBeTrue();
        info.JsonOutput.ShouldBeFalse();
    }
}
