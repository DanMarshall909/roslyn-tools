using System.CommandLine;
using RoslynTools.Commands;

namespace RoslynTools.Tests.Refactor;

public class Move
{
    [Fact]
    [Trait("Concern", "Metadata")]
    public void examples_include_to_project_usage()
    {
        var examples = MoveCommand.Info.Examples;
        Assert.Contains(examples, e => e.Contains("--to-project"));
    }

    [Fact]
    [Trait("Concern", "Metadata")]
    public void to_project_option_is_registered_with_optional_to_namespace_noted()
    {
        var cmd = MoveCommand.Build();

        var toProjectOpt = cmd.Options.FirstOrDefault(o => o.Name == "to-project");
        Assert.NotNull(toProjectOpt);

        // --to-namespace description must mention it is optional when --to-project is supplied
        var toNsOpt = cmd.Options.FirstOrDefault(o => o.Name == "to-namespace");
        Assert.NotNull(toNsOpt);
        Assert.Contains("--to-project", toNsOpt.Description, StringComparison.OrdinalIgnoreCase);
    }
}
