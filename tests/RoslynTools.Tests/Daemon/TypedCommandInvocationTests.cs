using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Daemon;

public sealed class TypedCommandInvocationTests
{
    private static readonly ApplicationCommandDefinition Definition = new(
        new RtCommandInfo(
            "static-class-inventory",
            false,
            true,
            "inspect",
            "Inventory static classes.",
            [],
            Aliases: ["static-types"]),
        typeof(FakeRequest),
        typeof(FakeResponse),
        mcpVisible: true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        []);

    [Theory]
    [InlineData("static-class-inventory")]
    [InlineData("static-types")]
    [InlineData("inspect", "static-class-inventory")]
    [InlineData("inspect", "static-types")]
    public void Typed_command_invocations_are_discovered_from_definitions(params string[] arguments)
    {
        TypedCommandInvocation.IsMatch(arguments, [Definition]).ShouldBeTrue();
    }

    [Theory]
    [InlineData()]
    [InlineData("inspect")]
    [InlineData("analyze", "static-class-inventory")]
    [InlineData("inspect", "outline")]
    public void Unrelated_invocations_are_not_claimed(params string[] arguments)
    {
        TypedCommandInvocation.IsMatch(arguments, [Definition]).ShouldBeFalse();
    }

    private sealed record FakeRequest;
    private sealed record FakeResponse;
}
