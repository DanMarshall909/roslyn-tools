using RoslynTools.Commands;

namespace RoslynTools.Tests.Refactor;

public class DestaticClass
{
    [Fact]
    [Trait("Concern", "Metadata")]
    public void command_metadata_category_is_refactor_and_mutates()
    {
        var info = DestaticClassCommand.Info;

        Assert.Equal("convert-static-class", info.Name);
        Assert.Equal("refactor", info.Category);
        Assert.True(info.Mutates);
        Assert.False(info.JsonOutput);
    }
}
