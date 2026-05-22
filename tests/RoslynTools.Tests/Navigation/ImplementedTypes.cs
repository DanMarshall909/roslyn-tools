using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

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
                .FindAsync(solution, symbol.ShouldBeAssignableTo<INamedTypeSymbol>()!);

            var worker = Assert.Single(results);
            var directInterfaces = worker["directInterfaces"].ShouldBeAssignableTo<List<string>>()!;
            var locations = worker["locations"].ShouldBeAssignableTo<List<Dictionary<string, object?>>>()!;

            worker["name"].ShouldBe("ConcreteWorker");
            worker["fqn"].ShouldBe("App.ConcreteWorker");
            worker["typeKind"].ShouldBe("Class");
            worker["accessibility"].ShouldBe("Public");
            worker["isAbstract"].ShouldBe(false);
            worker["project"].ShouldBe("TestProject");
            directInterfaces.ShouldContain("App.IWorker");
            worker["baseType"].ShouldBe("App.WorkerBase");
            locations.ShouldNotBeEmpty();
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
                .FindAsync(solution, symbol.ShouldBeAssignableTo<INamedTypeSymbol>()!);

            var handler = Assert.Single(results);

            handler["name"].ShouldBe("CreateHandler");
            handler["fqn"].ShouldBe("App.CreateHandler");
            handler["typeKind"].ShouldBe("Class");
            handler["isAbstract"].ShouldBe(false);
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
                .FindAsync(solution, symbol.ShouldBeAssignableTo<INamedTypeSymbol>()!);

            results.ShouldBeEmpty();
        }
    }
}
