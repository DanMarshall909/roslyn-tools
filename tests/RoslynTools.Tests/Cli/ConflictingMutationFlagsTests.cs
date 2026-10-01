using System.CommandLine;
using RoslynTools.Commands;
using Shouldly;

namespace RoslynTools.Tests.Cli;

public sealed class ConflictingMutationFlagsTests
{
    [Theory]
    [InlineData("add-dependency", "--type", "Service", "--dependency", "IDependency")]
    [InlineData("make-sealed", "--type", "Service", null, null)]
    [InlineData("remove-unused-usings", null, null, null, null)]
    public void Explicit_dry_run_cannot_be_combined_with_apply(
        string commandName,
        string? firstOption,
        string? firstValue,
        string? secondOption,
        string? secondValue)
    {
        var command = Build(commandName);
        var arguments = new[] { commandName, firstOption, firstValue, secondOption, secondValue }
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        var root = new RootCommand { command };

        var preview = root.Parse([.. arguments, "--dry-run"]);
        var apply = root.Parse([.. arguments, "--apply"]);
        var conflicting = root.Parse([.. arguments, "--dry-run", "--apply"]);

        preview.Errors.ShouldBeEmpty();
        apply.Errors.ShouldBeEmpty();
        conflicting.Errors.ShouldHaveSingleItem().Message.ShouldContain("--dry-run");
        conflicting.Errors[0].Message.ShouldContain("--apply");
    }

    private static Command Build(string name) => name switch
    {
        "add-dependency" => new AddDependencyCommand(null!, null!).Build(),
        "make-sealed" => new MakeSealedCommand(null!, null!).Build(),
        "remove-unused-usings" => new RemoveUnusedUsingsCommand(null!, null!).Build(),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };
}
