using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Tests.Navigation;

public sealed class ImplementedTypes
{
    [Fact]
    public async Task include_interface_base_type_and_location_details()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Workers.cs", """
                                        namespace App;

                                        public interface IWorker
                                        {
                                            void Work();
                                        }

                                        public abstract class WorkerBase
                                        {
                                        }

                                        public class ConcreteWorker : WorkerBase, IWorker
                                        {
                                            public void Work() { }
                                        }
                                        """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var symbol = await SymbolLocator.FindSymbolByFqnAsync(solution, "App.IWorker", "type");
            var results = await new FindImplementationsService()
                .FindAsync(solution, Assert.IsAssignableFrom<INamedTypeSymbol>(symbol));

            var worker = Assert.Single(results);
            var directInterfaces = Assert.IsAssignableFrom<List<string>>(worker["directInterfaces"]);
            var locations = Assert.IsAssignableFrom<List<Dictionary<string, object?>>>(worker["locations"]);

            Assert.Equal("ConcreteWorker", worker["name"]);
            Assert.Equal("App.ConcreteWorker", worker["fqn"]);
            Assert.Equal("Class", worker["typeKind"]);
            Assert.Equal("Public", worker["accessibility"]);
            Assert.Equal(false, worker["isAbstract"]);
            Assert.Equal("TestProject", worker["project"]);
            Assert.Contains("App.IWorker", directInterfaces);
            Assert.Equal("App.WorkerBase", worker["baseType"]);
            Assert.NotEmpty(locations);
        }
    }

    [Fact]
    public async Task include_classes_derived_from_an_abstract_base()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Handlers.cs", """
                                         namespace App;

                                         public abstract class Handler
                                         {
                                             public abstract void Handle();
                                         }

                                         public class CreateHandler : Handler
                                         {
                                             public override void Handle() { }
                                         }
                                         """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var symbol = await SymbolLocator.FindSymbolByFqnAsync(solution, "App.Handler", "type");
            var results = await new FindImplementationsService()
                .FindAsync(solution, Assert.IsAssignableFrom<INamedTypeSymbol>(symbol));

            var handler = Assert.Single(results);

            Assert.Equal("CreateHandler", handler["name"]);
            Assert.Equal("App.CreateHandler", handler["fqn"]);
            Assert.Equal("Class", handler["typeKind"]);
            Assert.Equal(false, handler["isAbstract"]);
        }
    }

    [Fact]
    public async Task return_no_results_for_a_concrete_class_without_children()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Worker.cs", """
                                       namespace App;

                                       public class Worker
                                       {
                                       }
                                       """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var symbol = await SymbolLocator.FindSymbolByFqnAsync(solution, "App.Worker", "type");
            var results = await new FindImplementationsService()
                .FindAsync(solution, Assert.IsAssignableFrom<INamedTypeSymbol>(symbol));

            Assert.Empty(results);
        }
    }
}
