using RoslynTools.Commands;

namespace RoslynTools.Tests.Issues.EP_006_StaticToInstanceMigration;

/// <summary>
/// US-023: Add `rt refactor destatic-class` command.
///
/// Drops the static modifier from a class, makes members instance, generates a matching
/// interface, registers the class in DI, and rewrites all call sites to use injected
/// references.
/// </summary>
[Trait("Epic", "EP-006")]
[Trait("Story", "US-023")]
public class DestaticClass
{
    [Fact]
    [Trait("Concern", "Metadata")]
    public void with_command_metadata_category_is_refactor_and_mutates()
    {
        var info = DestaticClassCommand.Info;

        Assert.Equal("destatic-class", info.Name);
        Assert.Equal("refactor", info.Category);
        Assert.True(info.Mutates);
        Assert.False(info.JsonOutput);
    }
}
