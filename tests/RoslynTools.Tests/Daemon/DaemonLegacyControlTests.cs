using System.Text.Json;
using RoslynTools.Daemon;
using Shouldly;

namespace RoslynTools.Tests.Daemon;

public sealed class DaemonLegacyControlTests
{
    [Theory]
    [InlineData("inspect", "outline")]
    [InlineData("refactor", "rename")]
    [InlineData("stop", "extra")]
    public async Task Legacy_application_arguments_are_rejected_without_dispatch(params string[] arguments)
    {
        using var output = new StringWriter();

        var stopping = await DaemonServer.HandleLegacyControlRequestAsync(
            new DaemonRequest("rejected", arguments, new GitState("unknown", false, null)),
            output);

        stopping.ShouldBeFalse();
        var responses = ReadResponses(output);
        responses.Length.ShouldBe(2);
        responses[0].Type.ShouldBe("stderr");
        responses[0].Data.ShouldNotBeNull();
        responses[0].Data!.ShouldContain("not supported");
        responses[1].Type.ShouldBe("exit");
        responses[1].Code.ShouldBe(1);
    }

    [Fact]
    public async Task Ping_remains_a_control_request()
    {
        using var output = new StringWriter();

        var stopping = await DaemonServer.HandleLegacyControlRequestAsync(
            new DaemonRequest("ping", ["ping"], new GitState("unknown", false, null)),
            output);

        stopping.ShouldBeFalse();
        var responses = ReadResponses(output);
        responses.Select(response => response.Type).ShouldBe(["stdout", "exit"]);
        responses[0].Data.ShouldBe("pong\n");
        responses[1].Code.ShouldBe(0);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("daemon", "stop")]
    public async Task Stop_remains_a_control_request(params string[] arguments)
    {
        using var output = new StringWriter();

        var stopping = await DaemonServer.HandleLegacyControlRequestAsync(
            new DaemonRequest("stop", arguments, new GitState("unknown", false, null)),
            output);

        stopping.ShouldBeTrue();
        var responses = ReadResponses(output);
        responses.Select(response => response.Type).ShouldBe(["stdout", "exit"]);
        responses[0].Data.ShouldBe("Daemon stopping.\n");
        responses[1].Code.ShouldBe(0);
    }

    private static DaemonResponse[] ReadResponses(StringWriter output) =>
        output.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<DaemonResponse>(line, DaemonProtocol.JsonOpts)!)
            .ToArray();
}
