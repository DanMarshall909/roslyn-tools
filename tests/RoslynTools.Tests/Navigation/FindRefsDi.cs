using Microsoft.Extensions.DependencyInjection;
using RoslynTools;
using RoslynTools.Core;

namespace RoslynTools.Tests.Navigation;

/// <summary>
///     Verifies that FindRefsCommand correctly resolves WorkspaceLoader from DI
///     when App.RunAsync is called with a service provider.
/// </summary>
public class FindRefsDi
{
    [Fact]
    public async Task finds_refs_command_with_di_service_provider_returns_exit_zero_and_results()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Model.cs", """
                                      namespace TestApp;

                                      public class Widget
                                      {
                                          public int Id { get; set; }
                                      }
                                      """);
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class WidgetService
                                        {
                                            public Widget Get(int id) => new Widget { Id = id };
                                        }
                                        """);

        var (workspace, sln) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var sc = new ServiceCollection();
            RoslynTools.Bootstrap.RegisterServices(sc);
            var services = sc.BuildServiceProvider();

            var (exitCode, stdout, _) = await InvokeAsync(
                ["find-refs", "--symbol", "Widget", "--solution", "ignored"],
                services);

            Assert.Equal(0, exitCode);
            Assert.Contains("Widget", stdout);
        }
    }

    [Fact]
    public async Task finds_refs_command_without_service_provider_falls_back_and_returns_results()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Model.cs", """
                                      namespace TestApp;

                                      public class Gadget { }
                                      """);
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class GadgetService
                                        {
                                            public Gadget Get() => new Gadget();
                                        }
                                        """);

        var (workspace, sln) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var (exitCode, stdout, _) = await InvokeAsync(
                ["find-refs", "--symbol", "Gadget", "--solution", "ignored"],
                null);

            Assert.Equal(0, exitCode);
            Assert.Contains("Gadget", stdout);
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> InvokeAsync(
        string[] args, IServiceProvider? services)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        Console.SetOut(stdout);
        Console.SetError(stderr);

        try
        {
            var exitCode = await App.RunAsync(args, services);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
