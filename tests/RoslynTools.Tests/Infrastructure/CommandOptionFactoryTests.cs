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

public class CommandOptionFactory_apply_skip
{
    [Fact]
    public void apply_option_is_named_apply_and_nullable_string()
    {
        var opt = CommandOptionFactory.CreateApplyOption();

        opt.Name.ShouldBe("apply");
        opt.ShouldBeOfType<Option<string?>>();
    }

    [Fact]
    public void apply_option_description_mentions_fix_ids()
    {
        var opt = CommandOptionFactory.CreateApplyOption();

        opt.Description.ShouldNotBeNull().ShouldContain("fix");
    }

    [Fact]
    public void skip_option_is_named_skip_and_nullable_string()
    {
        var opt = CommandOptionFactory.CreateSkipOption();

        opt.Name.ShouldBe("skip");
        opt.ShouldBeOfType<Option<string?>>();
    }

    [Fact]
    public void skip_option_description_mentions_fix_ids()
    {
        var opt = CommandOptionFactory.CreateSkipOption();

        opt.Description.ShouldNotBeNull().ShouldContain("fix");
    }
}
