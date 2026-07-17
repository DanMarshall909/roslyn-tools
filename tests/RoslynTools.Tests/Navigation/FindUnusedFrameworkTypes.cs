using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public class FindUnusedFrameworkTypes
{
    [Fact(DisplayName = "find-unused excludes a fixture whose base class declares facts")]
    public async Task excludes_a_fixture_whose_base_class_declares_facts()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Tests.cs", """
                                      using System;

                                      [AttributeUsage(AttributeTargets.Method)]
                                      public sealed class FactAttribute : Attribute { }

                                      public abstract class ContractTests
                                      {
                                          [Fact]
                                          public void contract_is_enforced() { }
                                      }

                                      public sealed class SqliteEventStoreTests : ContractTests { }
                                      """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(solution, "types");

            results.Select(result => result.Name).ShouldNotContain("SqliteEventStoreTests");
        }
    }

    [Fact(DisplayName = "find-unused excludes ASP.NET controllers")]
    public async Task excludes_asp_net_controllers()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Controllers.cs", """
                                            using System;

                                            public abstract class ControllerBase { }

                                            [AttributeUsage(AttributeTargets.Class)]
                                            public sealed class ApiControllerAttribute : Attribute { }

                                            public sealed class OrdersController : ControllerBase { }

                                            [ApiController]
                                            public sealed class HealthController { }
                                            """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(solution, "types");
            var names = results.Select(result => result.Name).ToList();

            names.ShouldNotContain("OrdersController");
            names.ShouldNotContain("HealthController");
        }
    }

    // A project whose registration goes through a reflective container. The scan is what
    // makes any instantiable type in the assembly a possible live registration.
    private const string AssemblyScannedProject = """
                                                  public sealed class ServiceCollection
                                                  {
                                                      public ServiceCollection FromAssemblies() => this;
                                                      public ServiceCollection AddClasses() => this;
                                                  }

                                                  public sealed class ReflectiveService { }

                                                  public static class Registration
                                                  {
                                                      public static void AddServices(ServiceCollection services)
                                                      {
                                                          services.FromAssemblies().AddClasses();
                                                      }
                                                  }
                                                  """;

    [Fact(DisplayName = "find-unused reports assembly-scanned types at low confidence rather than hiding them")]
    public async Task reports_instantiable_types_in_an_assembly_scanned_project_at_low_confidence()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services.cs", AssemblyScannedProject);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(solution, "types");

            var finding = results.SingleOrDefault(result => result.Name == "ReflectiveService");
            finding.ShouldNotBeNull(
                "suppressing the finding outright trades a false positive for a false negative; the caller should still see it");
            finding.Confidence.ShouldBe(UnusedConfidence.Low,
                "assembly scanning means the analysis cannot prove this type is dead");
        }
    }

    [Fact(DisplayName = "find-unused explains why an assembly-scanned finding is uncertain")]
    public async Task low_confidence_finding_states_its_reason()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services.cs", AssemblyScannedProject);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(solution, "types");

            var finding = results.Single(result => result.Name == "ReflectiveService");
            finding.Reason.ShouldNotBe(UnusedReason.NoSourceReferences,
                "the caller decides what to do with a low-confidence finding, so the reason must say what the analysis could not see");
            finding.Reason.ShouldContain("scan", Case.Insensitive,
                "the reason should name the reflective mechanism that makes this uncertain");
        }
    }

    [Fact(DisplayName = "find-unused excludes the type declaring the entry point")]
    public async Task excludes_the_type_that_declares_main()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Program.cs", """
                                        public static class Program
                                        {
                                            public static void Main(string[] args) { }
                                        }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(solution, "types");

            results.Select(result => result.Name).ShouldNotContain("Program",
                "the runtime invokes Main, so the entry-point type has no source reference and deleting it breaks the build");
        }
    }

    [Fact(DisplayName = "find-unused excludes a static class whose extension method is called")]
    public async Task excludes_extension_class_whose_extension_method_is_called()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Extensions.cs", """
                                           public static class StringExtensions
                                           {
                                               public static bool IsBlank(this string value) => value.Length == 0;
                                           }

                                           public class Caller
                                           {
                                               public bool Run(string s) => s.IsBlank();
                                           }
                                           """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(solution, "types");

            results.Select(result => result.Name).ShouldNotContain("StringExtensions",
                "an extension call names the method, never the containing class, so the type has zero references while being live");
        }
    }

    [Fact(DisplayName = "find-unused still reports a static class whose extension method is never called")]
    public async Task reports_extension_class_whose_extension_method_is_never_called()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Extensions.cs", """
                                           public static class DeadExtensions
                                           {
                                               public static bool IsBlank(this string value) => value.Length == 0;
                                           }
                                           """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(solution, "types");

            results.Select(result => result.Name).ShouldContain("DeadExtensions",
                "an extension class nobody calls is genuinely dead and must still be reported");
        }
    }

    [Fact(DisplayName = "find-unused reports a type in a non-reflective project at high confidence")]
    public async Task type_in_a_project_without_reflection_is_reported_at_high_confidence()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Models.cs", "public sealed class OrphanType { }");

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(solution, "types");

            var finding = results.Single(result => result.Name == "OrphanType");
            finding.Confidence.ShouldBe(UnusedConfidence.High,
                "nothing in this project constructs types reflectively, so zero references really does mean dead");
        }
    }
}
