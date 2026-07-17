using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

/// <summary>
/// US-139: the probe executes a slice of user code, so its failure modes matter as much as its
/// findings. These cover the paths that do not need a built assembly; the full probe is verified
/// end-to-end against rt's own Bootstrap.RegisterServices.
/// </summary>
public class ProbeDi
{
    private const string RegistrationSource = """
                                              namespace App;

                                              public sealed class ServiceCollection { }

                                              public static class Bootstrap
                                              {
                                                  public static void RegisterServices(ServiceCollection services) { }
                                              }
                                              """;

    [Theory]
    [InlineData("NoDotHere")]
    [InlineData(".LeadingDot")]
    [InlineData("TrailingDot.")]
    public void rejects_an_entrypoint_that_is_not_type_dot_method(string entrypoint)
    {
        Action act = () => ProbeDiService.SplitEntrypoint(entrypoint);

        act.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("Type.Method", Case.Sensitive,
                "a malformed --entrypoint is user error and must say what shape was expected");
    }

    [Fact]
    public void splits_a_fully_qualified_entrypoint_on_its_last_dot()
    {
        var (typeName, methodName) = ProbeDiService.SplitEntrypoint("App.Deep.Bootstrap.RegisterServices");

        typeName.ShouldBe("App.Deep.Bootstrap");
        methodName.ShouldBe("RegisterServices",
            "splitting on the first dot would break every namespaced entrypoint");
    }

    [Fact]
    public async Task reports_a_clear_error_when_the_entrypoint_does_not_exist()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Bootstrap.cs", RegistrationSource);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var act = () => new ProbeDiService().ProbeAsync(solution, "App.Bootstrap.NoSuchMethod");

            (await act.ShouldThrowAsync<InvalidOperationException>())
                .Message.ShouldContain("was not found", Case.Sensitive,
                    "a typo'd entrypoint should name what was not found, not surface a reflection error");
        }
    }

    [Fact]
    public async Task requires_a_built_solution_and_says_so()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Bootstrap.cs", RegistrationSource);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            // The ad-hoc workspace compiles in memory and emits no assembly, which is exactly the
            // state of an unbuilt solution.
            var act = () => new ProbeDiService().ProbeAsync(solution, "App.Bootstrap.RegisterServices");

            (await act.ShouldThrowAsync<InvalidOperationException>())
                .Message.ShouldContain("Build the solution", Case.Sensitive,
                    "US-139 AC: a missing assembly must fail with a clear message, not a load exception");
        }
    }

    [Fact]
    public async Task finds_an_entrypoint_by_its_fully_qualified_name()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Bootstrap.cs", RegistrationSource);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var (_, method) = await ProbeDiService.FindEntrypointAsync(solution, "App.Bootstrap", "RegisterServices");

            method.Name.ShouldBe("RegisterServices");
            method.ContainingType.Name.ShouldBe("Bootstrap");
        }
    }

    [Fact]
    public async Task finds_an_entrypoint_by_its_short_name()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Bootstrap.cs", RegistrationSource);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var (_, method) = await ProbeDiService.FindEntrypointAsync(solution, "Bootstrap", "RegisterServices");

            method.ContainingType.ToDisplayString().ShouldBe("App.Bootstrap",
                "the documented example uses the short form, so it has to resolve");
        }
    }
}
