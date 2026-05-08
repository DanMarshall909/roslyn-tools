using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;

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
            var symbol = Assert.IsAssignableFrom<INamedTypeSymbol>(
                compilation!.GetTypeByMetadataName("TestApp.Repository`1"));
            var result = new InspectService().BuildInspection(symbol, solution);

            var typeParameters = Assert.IsAssignableFrom<List<string>>(result["typeParameters"]);
            var constructors = Assert.IsAssignableFrom<List<Dictionary<string, object?>>>(result["constructors"]);
            var constructor = Assert.Single(constructors);
            var parameters = Assert.IsAssignableFrom<List<Dictionary<string, object?>>>(constructor["parameters"]);

            Assert.Equal(["TItem"], typeParameters);
            Assert.Contains("Repository<TItem>.Repository(string name, TItem seed)", constructor["signature"]?.ToString());
            Assert.Equal(["name", "seed"], parameters.Select(parameter => parameter["name"]).ToArray());
        }
    }
}
