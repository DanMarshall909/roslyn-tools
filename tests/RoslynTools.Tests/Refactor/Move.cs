using System.CommandLine;
using RoslynTools.Commands;
using RoslynTools.Services;

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

        var toNsOpt = cmd.Options.FirstOrDefault(o => o.Name == "to-namespace");
        Assert.NotNull(toNsOpt);
        Assert.Contains("--to-project", toNsOpt.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Concern", "Validation")]
    public async Task unknown_target_project_reports_project_name_and_throws()
    {
        const string src = """
                           namespace Foo;
                           public class Bar { }
                           """;

        using var project = new AdhocProjectBuilder("SourceProject")
            .AddSource("Bar.cs", src);

        var (workspace, solution) = project.Build();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new MoveService().ExecuteCrossProjectMoveAsync(
                workspace, solution,
                "Foo.Bar",
                "NonExistentProject",
                null,
                false));

        Assert.Contains("NonExistentProject", ex.Message);
    }
}
