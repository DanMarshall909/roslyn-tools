using RoslynTools.Commands;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Refactor;

public sealed class Move : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;

    public Move(RtServicesFixture services) => _services = services;

    [Fact]
    public void examples_include_to_project_usage()
    {
        MoveCommand.Info.Examples.ShouldContain(example => example.Contains("--to-project"));
    }

    [Fact]
    public void to_project_option_is_registered_with_optional_to_namespace_noted()
    {
        var command = _services.Resolve<MoveCommand>().Build();
        var toProject = command.Options.FirstOrDefault(option => option.Name == "to-project");
        var toNamespace = command.Options.FirstOrDefault(option => option.Name == "to-namespace");

        toProject.ShouldNotBeNull();
        toNamespace.ShouldNotBeNull();
        toNamespace.Description!.ShouldContain("--to-project", Case.Insensitive);
    }

    [Fact]
    public void move_file_option_is_documented_as_incompatible_with_to_project()
    {
        var command = _services.Resolve<MoveCommand>().Build();
        command.Options.ShouldContain(option => option.Name == "move-file");
        MoveCommand.Info.WhenToUse.ShouldContain("forbids --move-file", Case.Insensitive);
    }
}
