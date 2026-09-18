using RoslynTools;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class CliInvocationClassifierTests
{
    [Theory]
    [InlineData("doctor")]
    [InlineData("inspect", "doctor")]
    [InlineData("trust", "list")]
    [InlineData("refactor", "trust", "list")]
    [InlineData("--json", "refactor", "trust", "list")]
    [InlineData("maintainer", "daemon", "status")]
    [InlineData("config")]
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
    [InlineData("doctor")]
    [InlineData("trust", "list")]
    [InlineData("refactor", "trust", "list")]
    [InlineData("--json", "refactor", "trust", "list")]
    [InlineData("trusted", "probe-di")]
    public void local_and_trusted_invocations_run_in_process(params string[] arguments)
    {
        CliInvocationClassifier.MustRunInProcess(arguments).ShouldBeTrue();
    }

    [Theory]
    [InlineData("inspect", "outline")]
    [InlineData("analyze", "find-unused")]
    [InlineData("--unknown", "trusted")]
    public void ordinary_and_unrecognized_invocations_can_use_the_daemon(params string[] arguments)
    {
        CliInvocationClassifier.MustRunInProcess(arguments).ShouldBeFalse();
    }

    [Fact]
    public void Configuration_follows_the_selected_solution_repository()
    {
        var currentDirectory = Path.Combine(Path.GetTempPath(), "caller");
        var selectedDirectory = Path.Combine(Path.GetTempPath(), "selected");
        var solution = Path.Combine(selectedDirectory, "App.slnx");

        CliInvocationClassifier.ResolveConfigurationDirectory(
                ["inspect", "outline", "--solution", solution],
                currentDirectory)
            .ShouldBe(selectedDirectory);
    }

    [Fact]
    public void Relative_solution_resolves_under_the_selected_directory()
    {
        var currentDirectory = Path.Combine(Path.GetTempPath(), "caller");
        var selectedDirectory = Path.Combine(Path.GetTempPath(), "selected");

        CliInvocationClassifier.ResolveConfigurationDirectory(
                ["inspect", "outline", "--directory", selectedDirectory, "--solution=src/App.slnx"],
                currentDirectory)
            .ShouldBe(Path.Combine(selectedDirectory, "src"));
    }
}
