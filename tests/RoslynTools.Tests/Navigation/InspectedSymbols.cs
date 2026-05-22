using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class InspectedSymbols
{
    [Fact]
    public async Task include_generic_parameters_and_explicit_constructors()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Repository.cs", """
                                           namespace TestApp;

                                           public class Repository<TItem>
                                           {
                                               public Repository(string name, TItem seed)
                                               {
                                               }
                                           }
                                           """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var compilation = await solution.Projects.Single().GetCompilationAsync();
            var symbol = compilation!.GetTypeByMetadataName("TestApp.Repository`1").ShouldBeAssignableTo<INamedTypeSymbol>()!;
            var result = new InspectService().BuildInspection(symbol, solution);

            var typeParameters = result["typeParameters"].ShouldBeAssignableTo<List<string>>()!;
            var constructors = result["constructors"].ShouldBeAssignableTo<List<Dictionary<string, object?>>>()!;
            var constructor = Assert.Single(constructors);
            var parameters = constructor["parameters"].ShouldBeAssignableTo<List<Dictionary<string, object?>>>()!;

            typeParameters.ShouldBe(["TItem"]);
            constructor["signature"]?.ToString()!.ShouldContain("Repository<TItem>.Repository(string name, TItem seed)", Case.Sensitive);
            parameters.Select(parameter => parameter["name"]).ToArray().ShouldBe(["name", "seed"]);
        }
    }
}
