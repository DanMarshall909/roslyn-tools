using System.CommandLine;
using RoslynTools.Commands;

namespace RoslynTools.Tests.Infrastructure;

public class CommandOptionFactory_solution_overload
{
    [Fact]
    public void custom_help_text_is_reflected_in_returned_option()
    {
        var opt = CommandOptionFactory.CreateSolutionOption("My custom help");

        Assert.Equal("solution", opt.Name);
        Assert.Equal("My custom help", opt.Description);
    }

    [Fact]
    public void returned_option_is_nullable_string()
    {
        var opt = CommandOptionFactory.CreateSolutionOption("help");

        Assert.IsType<Option<string?>>(opt);
    }

    [Fact]
    public void parameterless_overload_preserves_canonical_description()
    {
        var opt = CommandOptionFactory.CreateSolutionOption();

        Assert.Equal("Path to .sln/.slnx", opt.Description);
    }
}
