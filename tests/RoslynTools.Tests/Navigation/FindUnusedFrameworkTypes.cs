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

    [Fact(DisplayName = "find-unused suppresses instantiable types in an assembly-scanned project")]
    public async Task suppresses_instantiable_types_in_an_assembly_scanned_project()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services.cs", """
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
                                         """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(solution, "types");

            results.Select(result => result.Name).ShouldNotContain("ReflectiveService");
        }
    }
}
