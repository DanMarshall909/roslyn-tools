using RoslynTools;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class CliInvocationClassifierTests
{
    [Theory]
    [InlineData("config")]
    [InlineData("--json", "config")]
    [InlineData("inspect", "config")]
    [InlineData("doctor")]
    [InlineData("inspect", "doctor")]
    [InlineData("trust", "list")]
    [InlineData("refactor", "trust", "list")]
    [InlineData("--json", "refactor", "trust", "list")]
    [InlineData("maintainer", "daemon", "status")]
    [InlineData("dump-help")]
    [InlineData("prime")]
    [InlineData("suggest", "rename a symbol")]
    [InlineData("--json", "doctor", "--installation-only")]
    [InlineData("--json", "trust", "list")]
    [InlineData("--decisions", "doctor.yml", "doctor", "--installation-only")]
    [InlineData("--decisions=doctor.yml", "doctor", "--installation-only")]
    [InlineData("inspect", "--json", "doctor")]
    public void local_only_invocations_do_not_eagerly_register_msbuild(params string[] arguments)
    {
        CliInvocationClassifier.RequiresEagerMsBuildRegistration(arguments).ShouldBeFalse();
    }

    [Theory]
    [InlineData("inspect", "outline")]
    [InlineData("analyze", "find-unused")]
    [InlineData("refactor", "rename")]
    [InlineData("trusted", "probe-di")]
    [InlineData("--decisions", "doctor", "inspect", "outline")]
    [InlineData("inspect", "outline", "--solution", "doctor")]
    [InlineData("--unknown", "doctor")]
    [InlineData("--decisions")]
    [InlineData("Doctor")]
    public void workspace_invocations_still_register_msbuild(params string[] arguments)
    {
        CliInvocationClassifier.RequiresEagerMsBuildRegistration(arguments).ShouldBeTrue();
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--llm")]
    [InlineData("--markdown")]
    [InlineData("--no-suggest")]
    [InlineData("--relax-json")]
    [InlineData("--show-workspace-warnings")]
    [InlineData("--trust-repository")]
    [InlineData("--yaml")]
    public void every_boolean_global_option_can_precede_a_local_command(string option)
    {
        CliInvocationClassifier.RequiresEagerMsBuildRegistration([option, "doctor"])
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData()]
    [InlineData("-h", "inspect", "outline")]
    [InlineData("--help", "inspect", "outline")]
    [InlineData("inspect", "outline", "--version")]
    public void help_version_and_empty_invocations_are_local(params string[] arguments)
    {
        CliInvocationClassifier.RequiresEagerMsBuildRegistration(arguments).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true, "config")]
    [InlineData(true, "--json", "config")]
    [InlineData(true, "inspect", "config")]
    [InlineData(true, "--decisions", "choices.yml", "inspect", "config")]
    [InlineData(false, "prime", "config")]
    [InlineData(false, "--solution", "config")]
    [InlineData(false, "--unknown", "config")]
    public void configuration_repair_exemption_uses_the_real_global_option_grammar(
        bool expected,
        params string[] arguments)
    {
        CliInvocationClassifier.IsConfigurationInspection(arguments).ShouldBe(expected);
    }
}
