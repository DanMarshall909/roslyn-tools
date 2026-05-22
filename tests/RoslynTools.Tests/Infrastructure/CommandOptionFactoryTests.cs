using System.CommandLine;
using RoslynTools.Commands;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public class CommandOptionFactory_solution_overload
{
    [Fact]
    public void custom_help_text_is_reflected_in_returned_option()
    {
        var opt = CommandOptionFactory.CreateSolutionOption("My custom help");

        opt.Name.ShouldBe("solution");
        opt.Description.ShouldBe("My custom help");
    }

    [Fact]
    public void returned_option_is_nullable_string()
    {
        var opt = CommandOptionFactory.CreateSolutionOption("help");

        opt.ShouldBeOfType<Option<string?>>();
    }

    [Fact]
    public void parameterless_overload_preserves_canonical_description()
    {
        var opt = CommandOptionFactory.CreateSolutionOption();

        opt.Description.ShouldBe("Path to .sln/.slnx");
    }
}
