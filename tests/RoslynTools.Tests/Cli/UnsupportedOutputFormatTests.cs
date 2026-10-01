using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Cli;

public class UnsupportedOutputFormatTests
{
    [Theory]
    [InlineData("analyze complexity", "complexity", "--llm")]
    [InlineData("complexity", "complexity", "--markdown")]
    [InlineData("cx", "complexity", "--yaml")]
    [InlineData("inspect doctor", "doctor", "--llm")]
    public async Task unsupported_explicit_format_is_rejected_before_workspace_load(
        string command,
        string canonicalName,
        string flag)
    {
        var missingSolution = Path.Combine(Path.GetTempPath(),
            $"rt-missing-format-test-{Guid.NewGuid():N}.slnx");

        var result = await RtCliHost.RunAsync(
            $"{command} {flag} --solution \"{missingSolution}\"");

        result.ExitCode.ShouldBe(1);
        result.Stdout.ShouldBe(string.Empty);
        result.Stderr.ShouldContain(canonicalName);
        result.Stderr.ShouldContain(flag);
        result.Stderr.ShouldContain("text");
        result.Stderr.ShouldContain("json");
        result.Stderr.ShouldNotContain(missingSolution);
    }

    [Fact]
    public async Task supported_format_wins_when_a_lower_priority_unsupported_flag_is_also_present()
    {
        var missingSolution = Path.Combine(Path.GetTempPath(),
            $"rt-missing-format-test-{Guid.NewGuid():N}.slnx");

        var result = await RtCliHost.RunAsync(
            $"analyze complexity --json --llm --solution \"{missingSolution}\"");

        result.ExitCode.ShouldBe(1);
        result.Stderr.ShouldNotContain("does not support");
        result.Stdout.ShouldContain(missingSolution);
    }

    [Fact]
    public async Task unsupported_format_on_a_public_subcommand_is_rejected()
    {
        var result = await RtCliHost.RunAsync("trust list --llm");

        result.ExitCode.ShouldBe(1);
        result.Stdout.ShouldBe(string.Empty);
        result.Stderr.ShouldContain("trust");
        result.Stderr.ShouldContain("--llm");
        result.Stderr.ShouldContain("text, json");
    }

    [Fact]
    public async Task parse_errors_take_precedence_over_unsupported_format()
    {
        var result = await RtCliHost.RunAsync("complexity --llm --bogus");

        result.ExitCode.ShouldBe(1);
        result.Stderr.ShouldContain("--bogus");
        result.Stderr.ShouldNotContain("does not support");
    }
}
